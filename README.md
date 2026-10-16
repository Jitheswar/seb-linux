# Safe Exam Browser for Linux

A Linux port of [Safe Exam Browser](https://safeexambrowser.org) (SEB) that runs
real online exams: it loads genuine `.seb` configurations, sends the
**Config Key** and **Browser Exam Key** to exam systems (Moodle *quizaccess_seb*,
Inspera, ...), exposes the SEB JavaScript API to exam pages and locks the
browser down according to the configuration.

Unlike the Windows and macOS clients, which embed a Chromium/WebKit runtime,
this port drives the **system Chromium** through the
[Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/).
It is written in C#/.NET 8 with **zero NuGet dependencies** in `src/`.

## Status

| Area | State |
| --- | --- |
| Configuration engine (plist XML, gzip, pswd/pwcc, Config Key) | ✅ verified against upstream & Moodle gold vectors |
| Browser control (CDP), request filter, exam key headers | ✅ verified end-to-end |
| SEB JavaScript API (`window.SafeExamBrowser`) | ✅ |
| Isolated / blocked clipboard, page zoom, print restrictions | ✅ |
| Quit URL (+ quit password), session reset, downloads | ✅ |
| Process monitor (forbidden applications), VM detection | ✅ |
| SEB Server (`seb-server`) integration | ⏳ not implemented |
| Certificate-encrypted configurations (`pkhs`/`phsk`) | ❌ not supported (see below) |
| Debian/RPM packages | ⏳ use `scripts/install.sh` for now |

## Quick start

Requirements: .NET 8 SDK and Chromium (or Google Chrome).

```sh
dotnet build
dotnet test

# Inspect a configuration
dotnet run --project src/SafeExamBrowser.Linux.Cli -- check exam.seb
dotnet run --project src/SafeExamBrowser.Linux.Cli -- info  exam.seb
dotnet run --project src/SafeExamBrowser.Linux.Cli -- keys  exam.seb --url https://moodle.example.org/

# Start an exam
dotnet run --project src/SafeExamBrowser.Linux.Cli -- run exam.seb
```

Install for the current user: `./scripts/install.sh` (binary: `~/.local/bin/seb-linux`).

### CLI

```
seb-linux <command> <configuration> [options]

Commands:
  run     Start an exam session with the given configuration
  info    Show the settings of the given configuration
  keys    Show the config key and browser exam key of the configuration
  check   Validate the given configuration
  version Show the program version
  help    Show this help

Options:
  --password <password>   The password for an encrypted configuration
  --no-kiosk              Launch the browser in a window instead of kiosk mode
  --headless              Launch the browser without a display server (testing)
  --chromium <path>       The Chromium executable to be used
  --url <url>             The URL for which the request hashes are to be shown
  --debug                 Enable debug logging
```

The configuration may be a file path, an `http(s)` URL or a `data:` URI. The
`keys` command prints the values an exam administrator needs for the
configuration of their LMS.

## How exams are verified

While an exam configuration is active, the browser behaves like SEB on
Windows/macOS:

- **User agent** contains `SEB/<version>`.
- **Request headers** (main frame and same-host requests):

  ```
  X-SafeExamBrowser-ConfigKeyHash: SHA256(urlWithoutFragment + ConfigKey)
  X-SafeExamBrowser-RequestHash:   SHA256(urlWithoutFragment + BrowserExamKey)
  ```

- **Browser Exam Key**: `HMAC-SHA256(salt, signature + version + ConfigKey)`
  with the same derivation as upstream (see `KeyGenerator`).
- **SEB JavaScript API**: `window.SafeExamBrowser` is injected into every
  document with the URL-specific key hashes and the SEB `updateKeys`
  mechanism, so exam systems can verify the client without relying on headers.

The hash values have been cross-checked against the gold vectors of the
Moodle `quizaccess_seb` plugin and upstream unit tests, and end-to-end against a
captured HTTP session.

> **Note:** the Browser Exam Key is bound to the SEB build version, exactly as
> with upstream. When the version changes, exam administrators must update the
> key in the LMS. Institutions that want a pinned value can set the environment
> variable `SEB_LINUX_BEK_SIGNATURE` to a stable string.

## Architecture

```
src/
  SafeExamBrowser.Linux.Configuration  Configuration file formats, Config Key / BEK
  SafeExamBrowser.Linux.Browser        Chromium control via DevTools Protocol
  SafeExamBrowser.Linux.Security       Process monitor, VM detection
  SafeExamBrowser.Linux.Client         Session orchestration
  SafeExamBrowser.Linux.Cli            seb-linux command line interface
tests/
  SafeExamBrowser.Linux.Tests          Unit tests incl. upstream-ported suites
```

Curious implementation details:

- Request filtering ports the upstream filter semantics (block rules first,
  block-by-default, automatic allow rule for the start URL).
- Because the renderer is paused while a document request is intercepted, the
  SEB JavaScript API layer for the next document is registered *before* the
  request is continued, using fire-and-forget DevTools commands to avoid a
  deadlock (the renderer does not answer while paused).
- With no display server available (or `SEB_HEADLESS=1`), the browser runs
  headless — used by the tests and by CI. Kiosk mode is selected from the
  configuration's window mode.

## Kiosk & lockdown

SEB locks down the browser, but a safe exam also needs a locked-down session.
On Linux the recommended setup is a kiosk compositor such as
[cage](https://github.com/cage-kiosk/cage):

```sh
cage -- seb-linux run exam.seb
```

See [docs/kiosk-session.md](docs/kiosk-session.md) for a complete exam-room
setup (dedicated user, autologin, screen capture, virtual terminals).

## Configuration compatibility

- Supported: plain plist XML, gzip-compressed and binary (`plnd`) SEB
  configurations, password-encrypted (`pswd`/`pwcc`) configurations,
  `seb://` and `sebs://` URL rewriting, configuration download detection.
- **Not supported:** certificate-encrypted configurations (RSA public key with
  password or certificate hash). Loading such a file reports `NotSupported`.
- SEB Server mode is recognized but not yet implemented; running a
  server-based configuration prints a clear error instead of failing silently.

## License

Mozilla Public License 2.0 (MPL-2.0), see [LICENSE](LICENSE) and
[NOTICE.md](NOTICE.md). This is an independent community port; "Safe Exam
Browser" is a trademark of ETH Zürich.
