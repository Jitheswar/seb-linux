# End-to-end tests

Run with:

```bash
scripts/e2e.sh
```

The harness starts a mock server (`server.py`) which serves both an exam page and a
mock SEB Server (Exam API v1) and drives the built `seb-linux` CLI through two
scenarios:

1. **normal** (`normal.seb`): the client loads the exam page, sends the SEB request
   headers (config key hash, request hash) and terminates via the quit URL. The mock
   server verifies the headers, the user agent and the SEB JavaScript API values.
2. **server** (`server-mode.seb`): the client connects to the mock SEB Server
   (`exam-api/discovery`, OAuth2 token, handshake with exam selection, app signature
   key exchange, exam configuration download, pings with instruction handling and
   connection close). The client is terminated by a `SEB_QUIT` instruction and the
   mock server verifies the whole API flow, including the app signature key, which
   is decrypted with the JCE reference implementation of the SEB Server
   (`AskDecrypt.java`, see `Cryptor.decryptASK` of the seb-server project).

Requirements: `python3`, `dotnet`, Chromium and (optionally, for the app signature
key check) a JDK. The port can be changed with `SEB_E2E_PORT`.

The configuration files use the fixed port 8765 for the start URL and the mock
server; the app signature key salt and the server browser exam key are constants
shared between `server.py`, the configurations and `scripts/e2e.sh`.
