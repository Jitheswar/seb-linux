#!/usr/bin/env python3
"""End-to-end test server for seb-linux.

Serves both a mock exam page and a mock SEB Server (Exam API v1) so that the whole
client stack can be exercised with `seb-linux run`:

- scenario "normal": the client loads a configuration file, loads the exam page,
  sends the SEB request headers and terminates via the quit URL.
- scenario "server": the client connects to the mock SEB Server, performs the
  handshake, exchanges an app signature key, downloads the exam configuration,
  loads the exam page and is terminated by a SEB_QUIT instruction.

The test script configures the expectations (scenario, config key, browser exam key)
via `POST /control/expect` before each client run and reads the scenario results via
`GET /control/summary` afterwards.
"""

import argparse
import base64
import hashlib
import json
import threading
import time

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs

# Values shared with scripts/e2e.sh and the mock SEB Server flow.
SALT = "00112233445566778899aabbccddeeff"
SERVER_BEK = "e2e-server-bek"
ACCESS_TOKEN = "e2e-access-token"
CONNECTION_TOKEN = "e2e-connection-token"
CLIENT_NAME = "e2e-client"
CLIENT_SECRET = "e2e-secret"
EXAM_ID = "1"
ASK_SIGNATURE = "safe-exam-browser-linux"
CONFIRMATION = "confirm-1"
NOTIFICATION_ID = 7

PAGE = """<!DOCTYPE html>
<html><head><title>SEB E2E</title></head>
<body><h1>SEB end-to-end test</h1><a id="e2e-tab" href="/tab-page" target="_blank">open tab</a>
<script>
(async () => {
  const out = { url: location.href, ua: navigator.userAgent };
  let page = { quit: false, waitTab: false };
  try { page = await (await fetch('/page-config', { cache: 'no-store' })).json(); } catch (e) {}
  try {
    out.seb = typeof window.SafeExamBrowser;
    out.version = window.SafeExamBrowser && window.SafeExamBrowser.version;
    out.hasUpdateKeys = typeof (window.SafeExamBrowser && window.SafeExamBrowser.security && window.SafeExamBrowser.security.updateKeys);
    out.configKey = window.SafeExamBrowser && window.SafeExamBrowser.security && window.SafeExamBrowser.security.configKey;
    out.browserExamKey = window.SafeExamBrowser && window.SafeExamBrowser.security && window.SafeExamBrowser.security.browserExamKey;
  } catch (e) { out.sebError = String(e); }
  try {
    const r = await fetch('/subresource', { cache: 'no-store' });
    out.subresource = r.status;
  } catch (e) { out.subresourceError = String(e); }
  out.tabReported = false;
  if (page.waitTab) {
    for (let i = 0; i < 60 && !out.tabReported; i++) {
      try { out.tabReported = (await (await fetch('/tab-status', { cache: 'no-store' })).json()).reported; } catch (e) {}
      if (!out.tabReported) { await new Promise(r => setTimeout(r, 100)); }
    }
  }
  try {
    const a = document.createElement('a');
    a.href = '/download-file';
    a.download = 'e2e-download.txt';
    document.body.appendChild(a);
    a.click();
    out.downloadTriggered = true;
  } catch (e) { out.downloadError = String(e); }
  await new Promise(r => setTimeout(r, 3000));
  try {
    await fetch('/report', { method: 'POST', cache: 'no-store', body: JSON.stringify(out) });
  } catch (e) {}
  if (page.quit) { location.href = '/quit'; }
})();
</script></body></html>"""

TAB_PAGE = """<!DOCTYPE html>
<html><head><title>SEB E2E tab</title></head>
<body><h1>SEB e2e tab</h1>
<script>
(async () => {
  const out = { url: location.href, ua: navigator.userAgent, tab: true };
  try {
    out.seb = typeof window.SafeExamBrowser;
    out.version = window.SafeExamBrowser && window.SafeExamBrowser.version;
    out.hasUpdateKeys = typeof (window.SafeExamBrowser && window.SafeExamBrowser.security && window.SafeExamBrowser.security.updateKeys);
    out.configKey = window.SafeExamBrowser && window.SafeExamBrowser.security && window.SafeExamBrowser.security.configKey;
    out.browserExamKey = window.SafeExamBrowser && window.SafeExamBrowser.security && window.SafeExamBrowser.security.browserExamKey;
  } catch (e) { out.sebError = String(e); }
  try {
    await fetch('/tab-report', { method: 'POST', cache: 'no-store', body: JSON.stringify(out) });
  } catch (e) {}
})();
</script></body></html>"""

DISCOVERY = {
    "title": "Safe Exam Browser Server / Exam API Description",
    "description": "End-to-end mock of the SEB Server Exam API",
    "server-location": "http://127.0.0.1/",
    "api-versions": [
        {
            "name": "v1",
            "endpoints": [
                {"name": "access-token-endpoint", "description": "request OAuth2 access token", "location": "/oauth/token", "authorization": "Basic"},
                {"name": "seb-handshake-endpoint", "description": "handshake", "location": "/exam-api/v1/handshake", "authorization": "Bearer"},
                {"name": "seb-configuration-endpoint", "description": "exam configuration", "location": "/exam-api/v1/examconfig", "authorization": "Bearer"},
                {"name": "seb-ping-endpoint", "description": "pings", "location": "/exam-api/v1/sebping", "authorization": "Bearer"},
                {"name": "seb-log-endpoint", "description": "log events", "location": "/exam-api/v1/seblog", "authorization": "Bearer"},
            ],
        }
    ],
}

EXAMS = [
    {"examId": 1, "name": "E2E Exam", "url": "http://127.0.0.1:8765/", "lmsType": "MOCK"}
]


def sha256_hex(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


class State:
    def __init__(self):
        self.lock = threading.Lock()
        self.reset()

    def reset(self):
        self.expect = None
        self.token_requests = 0
        self.handshake_post = None
        self.handshake_patch = None
        self.handshake_put = None
        self.put_ask = None
        self.exam_config_requests = 0
        self.pings = []
        self.deleted = False
        self.report = None
        self.tab_report = None
        self.header_checks = []


STATE = State()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass

    ###########################################################################
    # Helpers
    ###########################################################################

    def _body(self):
        length = int(self.headers.get("Content-Length", 0) or 0)
        return self.rfile.read(length).decode("utf-8", "replace")

    def _form(self):
        return {key: values[0] for key, values in parse_qs(self._body()).items()}

    def _respond(self, code, body=b"", content_type="application/json", headers=None):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        for name, value in (headers or {}).items():
            self.send_header(name, value)
        self.end_headers()
        if data:
            self.wfile.write(data)

    def _respond_json(self, value, code=200, headers=None):
        self._respond(code, json.dumps(value), "application/json", headers)

    def _check_page_headers(self, path):
        with STATE.lock:
            expect = STATE.expect
            if not expect or expect.get("mode") != "page":
                return
            url = expect["url"] + path.lstrip("/")
            expected_config_key = sha256_hex(url + expect["configKey"])
            expected_request_hash = sha256_hex(url + expect["browserExamKey"])
            config_key = self.headers.get("X-SafeExamBrowser-ConfigKeyHash")
            request_hash = self.headers.get("X-SafeExamBrowser-RequestHash")
            STATE.header_checks.append({
                "path": path,
                "url": url,
                "ok": config_key == expected_config_key and request_hash == expected_request_hash,
                "configKeyHash": config_key,
                "expectedConfigKeyHash": expected_config_key,
                "requestHash": request_hash,
                "expectedRequestHash": expected_request_hash,
            })

    def _check_api_authorization(self):
        return self.headers.get("Authorization") == f"Bearer {ACCESS_TOKEN}"

    def _check_connection_token(self):
        return self.headers.get("SEBConnectionToken") == CONNECTION_TOKEN

    ###########################################################################
    # Exam page
    ###########################################################################

    def do_GET(self):
        path = self.path.split("?")[0]

        if path == "/":
            self._check_page_headers("/")
            self._respond(200, PAGE, "text/html; charset=utf-8")
        elif path == "/page-config":
            self._check_page_headers("/page-config")
            with STATE.lock:
                expect = STATE.expect or {}
            self._respond_json({"quit": bool(expect.get("pageQuit")), "waitTab": not expect.get("noTabTest")})
        elif path == "/subresource":
            self._check_page_headers("/subresource")
            self._respond(200, "subresource-ok", "text/plain")
        elif path == "/tab-page":
            self._check_page_headers("/tab-page")
            self._respond(200, TAB_PAGE, "text/html; charset=utf-8")
        elif path == "/tab-status":
            with STATE.lock:
                self._respond_json({"reported": STATE.tab_report is not None})
        elif path == "/download-file":
            self._respond(200, "download-ok", "text/plain", headers={"Content-Disposition": 'attachment; filename="e2e-download.txt"'})
        elif path == "/quit":
            self._respond(200, "<html><body>quit</body></html>", "text/html; charset=utf-8")

        elif path == "/exam-api/discovery":
            self._respond_json(DISCOVERY)
        elif path == "/exam-api/v1/examconfig":
            self._exam_config()
        elif path == "/control/ping":
            self._respond_json({"ok": True})
        elif path == "/control/summary":
            with STATE.lock:
                self._respond_json(self._summarize())
        else:
            self._respond(404, "not found", "text/plain")

    ###########################################################################
    # SEB Server API and control
    ###########################################################################

    def do_POST(self):
        path = self.path.split("?")[0]

        if path == "/report":
            self._report()
        elif path == "/tab-report":
            self._tab_report()
        elif path == "/oauth/token":
            self._token()
        elif path == "/exam-api/v1/handshake":
            self._handshake_create()
        elif path == "/exam-api/v1/sebping":
            self._ping()
        elif path == "/exam-api/v1/seblog":
            self._body()
            self._respond(200, "{}")
        elif path == "/control/expect":
            self._expect()
        else:
            self._respond(404, "not found", "text/plain")

    def do_PATCH(self):
        path = self.path.split("?")[0]

        if path == "/exam-api/v1/handshake":
            with STATE.lock:
                STATE.handshake_patch = self._form()
            self._respond_json([], headers={"SEBExamSalt": SALT, "SEBServerBEK": SERVER_BEK})
        else:
            self._respond(404, "not found", "text/plain")

    def do_PUT(self):
        path = self.path.split("?")[0]

        if path == "/exam-api/v1/handshake":
            form = self._form()
            with STATE.lock:
                STATE.handshake_put = form
                STATE.put_ask = form.get("seb_signature_key")
            self._respond_json([], headers={"SEBServerBEK": SERVER_BEK})
        else:
            self._respond(404, "not found", "text/plain")

    def do_DELETE(self):
        path = self.path.split("?")[0]

        if path == "/exam-api/v1/handshake":
            form = self._form()
            with STATE.lock:
                STATE.deleted = form.get("delete") == "true"
            self._respond_json({})
        else:
            self._respond(404, "not found", "text/plain")

    def _token(self):
        expected = "Basic " + base64.b64encode(f"{CLIENT_NAME}:{CLIENT_SECRET}".encode()).decode()
        form = self._form()

        if self.headers.get("Authorization") != expected or form.get("grant_type") != "client_credentials":
            self._respond(401, '{"error": "invalid_client"}')
            return

        with STATE.lock:
            STATE.token_requests += 1

        self._respond_json({"access_token": ACCESS_TOKEN, "token_type": "bearer", "expires_in": 3600})

    def _handshake_create(self):
        form = self._form()

        if not self._check_api_authorization():
            self._respond(401, '{"error": "unauthorized"}')
            return

        with STATE.lock:
            STATE.handshake_post = form

        self._respond_json(EXAMS, headers={"SEBConnectionToken": CONNECTION_TOKEN})

    def _exam_config(self):
        query = parse_qs(self.path.split("?", 1)[1]) if "?" in self.path else {}

        if not self._check_api_authorization() or not self._check_connection_token() or query.get("examId") != [EXAM_ID]:
            self._respond(403, '{"error": "forbidden"}')
            return

        with STATE.lock:
            STATE.exam_config_requests += 1

        with open(EXAM_CONFIG, "rb") as file:
            self._respond(200, file.read(), "application/octet-stream")

    def _ping(self):
        form = self._form()

        if not self._check_api_authorization() or not self._check_connection_token():
            self._respond(403, '{"error": "forbidden"}')
            return

        with STATE.lock:
            STATE.pings.append(form)
            number = int(form.get("ping-number", "0"))
            reported = STATE.report is not None

        if number == 1:
            instruction = {
                "instruction": "NOTIFICATION_CONFIRM",
                "attributes": {"id": NOTIFICATION_ID, "type": "lockscreen", "instruction-confirm": CONFIRMATION},
            }
            self._respond_json(instruction)
        elif reported:
            self._respond_json({"instruction": "SEB_QUIT", "attributes": {}})
        else:
            self._respond(204)

    def _report(self):
        body = self._body()

        try:
            report = json.loads(body)
        except ValueError:
            self._respond(400, '{"error": "invalid json"}')
            return

        with STATE.lock:
            STATE.report = report

        self._respond(200, "ok", "text/plain")

    def _tab_report(self):
        body = self._body()

        try:
            report = json.loads(body)
        except ValueError:
            self._respond(400, '{"error": "invalid json"}')
            return

        with STATE.lock:
            STATE.tab_report = report

        self._respond(200, "ok", "text/plain")

    def _expect(self):
        try:
            expect = json.loads(self._body())
        except ValueError:
            self._respond(400, '{"error": "invalid json"}')
            return

        with STATE.lock:
            STATE.reset()
            STATE.expect = expect

        self._respond_json({"ok": True})

    ###########################################################################
    # Summary and checks
    ###########################################################################

    def _summarize(self):
        expect = STATE.expect or {}
        mode = expect.get("mode")
        checks = []

        def check(name, ok, detail=""):
            checks.append({"name": name, "passed": bool(ok), "detail": str(detail)})

        def is_valid_ask(ask):
            if not ask:
                return False
            try:
                data = bytes.fromhex(ask)
            except ValueError:
                return False
            return len(data) == 16 + len(ASK_SIGNATURE) + 16

        if mode == "page":
            page = STATE.report

            check("the exam page reported back", page is not None)

            if page:
                check("the user agent identifies as SEB", "SEB" in page.get("ua", ""), page.get("ua", ""))
                check("the SEB JavaScript API is available", page.get("seb") == "object" and page.get("hasUpdateKeys") == "function" and str(page.get("version", "")).startswith("SEB"), f"version={page.get('version')}")
                check("the configuration key hash (JavaScript API) matches", page.get("configKey") == sha256_hex(expect["url"] + expect["configKey"]), page.get("configKey"))
                check("the browser exam key hash (JavaScript API) matches", page.get("browserExamKey") == sha256_hex(expect["url"] + expect["browserExamKey"]), page.get("browserExamKey"))
                check("subresources are loaded", page.get("subresource") == 200, page.get("subresource"))
                if not expect.get("noTabTest"):
                    check("the new tab was opened and reported back", page.get("tabReported") is True and STATE.tab_report is not None, page.get("tabReported"))
                check("the download was triggered", page.get("downloadTriggered") is True, page.get("downloadError", ""))

                if not expect.get("noTabTest"):
                    tab = STATE.tab_report

                    if tab:
                        check("the user agent identifies as SEB in the new tab", "SEB" in tab.get("ua", ""), tab.get("ua", ""))
                        check("the SEB JavaScript API is available in the new tab", tab.get("seb") == "object" and tab.get("hasUpdateKeys") == "function", tab.get("version"))
                        check("the configuration key hash (new tab) matches", tab.get("configKey") == sha256_hex(expect["url"] + "tab-page" + expect["configKey"]), tab.get("configKey"))
                        check("the browser exam key hash (new tab) matches", tab.get("browserExamKey") == sha256_hex(expect["url"] + "tab-page" + expect["browserExamKey"]), tab.get("browserExamKey"))
        elif mode == "server":
            handshake = STATE.handshake_post or {}

            check("an OAuth2 access token was requested", STATE.token_requests > 0, f"{STATE.token_requests} request(s)")
            check("the handshake was performed for the configured exam", handshake.get("examId") == EXAM_ID, handshake.get("examId", "missing"))
            check("the handshake contained the client information", bool(handshake.get("client_id")) and bool(handshake.get("seb_version")) and bool(handshake.get("seb_machine_name")), json.dumps(handshake))
            check("the exam was selected", STATE.handshake_patch is not None and STATE.handshake_patch.get("examId") == EXAM_ID, json.dumps(STATE.handshake_patch))
            check("the connection was established", STATE.handshake_put is not None, json.dumps(STATE.handshake_put))
            check("a valid app signature key was sent", is_valid_ask(STATE.put_ask), STATE.put_ask or "missing")
            check("the exam configuration was downloaded", STATE.exam_config_requests == 1, f"{STATE.exam_config_requests} request(s)")
            check("the client sent pings", len(STATE.pings) >= 2, f"{len(STATE.pings)} ping(s)")

            if len(STATE.pings) >= 2:
                check("the instruction confirmation was sent with the next ping", STATE.pings[1].get("instruction-confirm") == CONFIRMATION, json.dumps(STATE.pings[1]))

            page = STATE.report

            check("the exam page reported back", page is not None)

            if page:
                check("the user agent identifies as SEB", "SEB" in page.get("ua", ""), page.get("ua", ""))
                check("the configuration key hash (JavaScript API) matches", page.get("configKey") == sha256_hex(expect["url"] + expect["configKey"]), page.get("configKey"))
                check("the server browser exam key hash (JavaScript API) matches", page.get("browserExamKey") == sha256_hex(expect["url"] + expect["browserExamKey"]), page.get("browserExamKey"))

                if not expect.get("noTabTest"):
                    check("the new tab was opened and reported back", page.get("tabReported") is True and STATE.tab_report is not None, page.get("tabReported"))

                if not expect.get("noTabTest"):
                    tab = STATE.tab_report

                    if tab:
                        check("the user agent identifies as SEB in the new tab", "SEB" in tab.get("ua", ""), tab.get("ua", ""))
                        check("the SEB JavaScript API is available in the new tab", tab.get("seb") == "object" and tab.get("hasUpdateKeys") == "function", tab.get("version"))
                        check("the configuration key hash (new tab) matches", tab.get("configKey") == sha256_hex(expect["url"] + "tab-page" + expect["configKey"]), tab.get("configKey"))
                        check("the browser exam key hash (new tab) matches", tab.get("browserExamKey") == sha256_hex(expect["url"] + "tab-page" + expect["browserExamKey"]), tab.get("browserExamKey"))

            check("the connection was closed (DELETE)", STATE.deleted)

        # Every recorded request must carry the correct SEB headers — including the first
        # document request of a new tab. An unsigned first request is the "login loop"
        # regression: an LMS that validates the SEB identity bounces such a tab back to its
        # login page.
        for header_check in STATE.header_checks:
            check(
                f"the SEB request headers for '{header_check['path']}' match",
                header_check["ok"],
                f"configKeyHash={header_check['configKeyHash']} (expected {header_check['expectedConfigKeyHash']}) requestHash={header_check['requestHash']} (expected {header_check['expectedRequestHash']})",
            )

        return {
            "scenario": expect.get("scenario"),
            "mode": mode,
            "passed": all(item["passed"] for item in checks) if checks else False,
            "checks": checks,
            "api": {
                "handshake": STATE.handshake_post,
                "selectExam": STATE.handshake_patch,
                "establishConnection": STATE.handshake_put,
                "appSignatureKey": STATE.put_ask,
                "pings": STATE.pings,
                "connectionClosed": STATE.deleted,
            },
        }


EXAM_CONFIG = "exam-config.seb"


def main():
    global EXAM_CONFIG

    parser = argparse.ArgumentParser(description="End-to-end test server for seb-linux")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--exam-config", default="exam-config.seb", help="the exam configuration served by the mock SEB Server")
    args = parser.parse_args()

    EXAM_CONFIG = args.exam_config

    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.daemon_threads = True

    print(f"[e2e] listening on 127.0.0.1:{args.port} (exam config: {EXAM_CONFIG})", flush=True)

    server.serve_forever()


if __name__ == "__main__":
    main()
