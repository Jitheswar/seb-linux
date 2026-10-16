#!/usr/bin/env bash
#
# End-to-end tests for exam-unlock (see tests/e2e/README.md).
#
# Scenario "normal":
#   exam-unlock run <normal.seb>: loads the exam page, sends the SEB request
#   headers (config key hash, request hash), opens a new tab (with its own
#   per-URL key hashes and the SEB JavaScript API), downloads a file and
#   terminates via the quit URL. The browser runs in a normal window (no
#   kiosk mode) and the session works on virtual machines.
#
# Scenario "server":
#   exam-unlock run <server-mode.seb>: connects to the mock SEB Server, performs
#   the handshake, exchanges an app signature key (verified with the JCE reference
#   implementation of the server), downloads the exam configuration, runs the exam
#   and is terminated by a SEB_QUIT instruction.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
E2E="$ROOT/tests/e2e"
PORT="${SEB_E2E_PORT:-8765}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/exam-unlock-e2e.XXXXXX")"
CLI="$ROOT/src/SafeExamBrowser.Linux.Cli/bin/Debug/net8.0/exam-unlock"

# The app signature key verifies the default build identifier.
unset SEB_LINUX_BEK_SIGNATURE

# Run the client with an isolated HOME so the browser profile and the
# download directory do not touch the real user data.
E2E_HOME="$WORK/home"
mkdir -p "$E2E_HOME"

fail() {
	echo "FAIL: $*" >&2
	exit 1
}

cleanup() {
	if [ -n "${SERVER_PID:-}" ]; then
		kill "$SERVER_PID" 2>/dev/null || true
		wait "$SERVER_PID" 2>/dev/null || true
	fi
}

control() {
	# control <method> <path> [json]
	python3 - "$PORT" "$1" "$2" "${3:-}" <<'PY'
import sys, urllib.request

port, method, path, data = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
url = f"http://127.0.0.1:{port}{path}"
request = urllib.request.Request(url, data=data.encode() if data else None, method=method)

if data:
    request.add_header("Content-Type", "application/json")

with urllib.request.urlopen(request, timeout=10) as response:
    print(response.read().decode())
PY
}

summary_field() {
	# summary_field <summary.json> <dotted-path>
	python3 - "$1" "$2" <<'PY'
import json, sys

with open(sys.argv[1]) as file:
    value = json.load(file)

for key in sys.argv[2].split("."):
    value = value[key]

print(value)
PY
}

run_summary() {
	# Prints the scenario checks and fails if the scenario did not pass.
	local summary="$1"
	local scenario="$2"

	python3 - "$summary" "$scenario" <<'PY'
import json, sys

with open(sys.argv[1]) as file:
    summary = json.load(file)

print(f"  scenario: {summary['scenario']} ({summary['mode']})")

for check in summary["checks"]:
    marker = "ok  " if check["passed"] else "FAIL"
    print(f"    [{marker}] {check['name']}")

if not summary["passed"]:
    sys.exit(1)
PY
}

echo "== building the CLI =="
dotnet build "$ROOT/src/SafeExamBrowser.Linux.Cli" -v q --nologo

[ -x "$CLI" ] || fail "the CLI has not been built ($CLI)"

# Virtual machine detection has been removed: on a VM the session must run
# exactly as on physical hardware.
if [ -r /sys/class/dmi/id/product_name ] \
   && grep -qiE 'vmware|virtualbox|kvm|qemu|innotek|xen|hyper-v|parallels|bochs|bhyve|virtual machine' \
      /sys/class/dmi/id/product_name /sys/class/dmi/id/sys_vendor \
      /sys/class/dmi/id/board_vendor /sys/class/dmi/id/bios_vendor 2>/dev/null; then
	echo "  (running inside a virtual machine: VM detection is disabled, the session must not be rejected)"
fi

echo "== starting the mock server on port $PORT =="
python3 "$E2E/server.py" --port "$PORT" --exam-config "$E2E/exam-config.seb" > "$WORK/server.log" 2>&1 &
SERVER_PID=$!
trap cleanup EXIT

for _ in $(seq 1 50); do
	if control GET /control/ping > /dev/null 2>&1; then
		break
	fi
	sleep 0.1
done

control GET /control/ping > /dev/null || fail "the mock server did not start (see $WORK/server.log)"

echo "== computing the expected keys =="
CONFIG_KEY_NORMAL="$("$CLI" keys "$E2E/normal.seb" | sed -n 's/^Config Key: *//p')"
BEK_NORMAL="$("$CLI" keys "$E2E/normal.seb" | sed -n 's/^Browser Exam Key: *//p')"
CONFIG_KEY_EXAM="$("$CLI" keys "$E2E/exam-config.seb" | sed -n 's/^Config Key: *//p')"

[ -n "$CONFIG_KEY_NORMAL" ] || fail "failed to determine the config key of normal.seb"
[ -n "$BEK_NORMAL" ] || fail "failed to determine the browser exam key of normal.seb"
[ -n "$CONFIG_KEY_EXAM" ] || fail "failed to determine the config key of exam-config.seb"

echo "  normal.seb:      config key $CONFIG_KEY_NORMAL, browser exam key $BEK_NORMAL"
echo "  exam-config.seb: config key $CONFIG_KEY_EXAM"

###############################################################################
# Scenario 1: normal configuration with quit URL termination
###############################################################################

echo "== scenario 1: normal configuration =="

control POST /control/expect "$(python3 - "$PORT" "$CONFIG_KEY_NORMAL" "$BEK_NORMAL" <<'PY'
import json, sys

port, config_key, browser_exam_key = sys.argv[1], sys.argv[2], sys.argv[3]
print(json.dumps({
    "scenario": "normal",
    "mode": "page",
    "url": f"http://127.0.0.1:{port}/",
    "configKey": config_key,
    "browserExamKey": browser_exam_key,
    "pageQuit": True,
}))
PY
)" > /dev/null

set +e
SEB_HEADLESS=1 HOME="$E2E_HOME" timeout 90 "$CLI" run "$E2E/normal.seb" --debug > "$WORK/normal.log" 2>&1
STATUS=$?
set -e

[ "$STATUS" -eq 0 ] || fail "scenario 1: the client exited with status $STATUS (see $WORK/normal.log)"

grep -q "Exam session ended (Terminated)" "$WORK/normal.log" || fail "scenario 1: the session was not terminated via the quit URL (see $WORK/normal.log)"

# The browser must run in a normal window, never in kiosk mode.
grep -q "Browser arguments:" "$WORK/normal.log" || fail "scenario 1: the browser arguments were not logged (see $WORK/normal.log)"
if grep "Browser arguments:" "$WORK/normal.log" | grep -q -- "--kiosk"; then
	fail "scenario 1: the browser was launched in kiosk mode (see $WORK/normal.log)"
fi

# Downloads must work and be stored on disk.
[ -f "$E2E_HOME/Downloads/e2e-download.txt" ] || fail "scenario 1: the download was not saved to disk (see $WORK/normal.log)"
grep -q "download-ok" "$E2E_HOME/Downloads/e2e-download.txt" || fail "scenario 1: the downloaded file has unexpected content"

control GET /control/summary > "$WORK/summary-normal.json"
run_summary "$WORK/summary-normal.json" normal || fail "scenario 1: server-side verification failed"

###############################################################################
# Scenario 2: SEB Server session with instruction-based termination
###############################################################################

echo "== scenario 2: SEB Server session =="

control POST /control/expect "$(python3 - "$PORT" "$CONFIG_KEY_EXAM" <<'PY'
import json, sys

port, config_key = sys.argv[1], sys.argv[2]
print(json.dumps({
    "scenario": "server",
    "mode": "server",
    "url": f"http://127.0.0.1:{port}/",
    "configKey": config_key,
    "browserExamKey": "e2e-server-bek",
    "pageQuit": False,
}))
PY
)" > /dev/null

set +e
SEB_HEADLESS=1 HOME="$E2E_HOME" timeout 90 "$CLI" run "$E2E/server-mode.seb" --debug > "$WORK/session.log" 2>&1
STATUS=$?
set -e

[ "$STATUS" -eq 0 ] || fail "scenario 2: the client exited with status $STATUS (see $WORK/session.log)"

grep -q "Exam session ended (Terminated)" "$WORK/session.log" || fail "scenario 2: the session was not terminated by the SEB_QUIT instruction (see $WORK/session.log)"
grep -q "SEB Server requested to terminate" "$WORK/session.log" || fail "scenario 2: no SEB_QUIT instruction was processed (see $WORK/session.log)"

control GET /control/summary > "$WORK/summary-server.json"
run_summary "$WORK/summary-server.json" server || fail "scenario 2: server-side verification failed"

# Verify the app signature key with the exact JCE code path of the SEB Server.
ASK="$(summary_field "$WORK/summary-server.json" api.appSignatureKey)"

if command -v javac > /dev/null && command -v java > /dev/null; then
	echo "  verifying the app signature key with the JCE reference implementation..."

	javac -d "$WORK" "$E2E/AskDecrypt.java"
	DECRYPTED="$(java -cp "$WORK" AskDecrypt decrypt "e2e-connection-token" "00112233445566778899aabbccddeeff" "$ASK")"

	[ "$DECRYPTED" = "safe-exam-browser-linux" ] || fail "scenario 2: the app signature key decrypted to '$DECRYPTED'"
	echo "    [ok  ] the app signature key decrypts to 'safe-exam-browser-linux'"
else
	echo "  (javac/java not available, skipping the app signature key decryption check)"
fi

echo
echo "PASS: all end-to-end scenarios succeeded"
