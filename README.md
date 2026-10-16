# exam-unlock

An **unlocked** Safe Exam Browser for **Linux and Windows**. It runs real
online exams exactly the way SEB does — it loads genuine `.seb`
configurations, sends the **Config Key** and **Browser Exam Key** to exam
systems (Moodle *quizaccess_seb*, Inspera, ...), exposes the SEB JavaScript
API to exam pages — but with **no lockdown**: there is no request filter,
no kiosk, no process or virtual-machine monitoring and no clipboard, print
or zoom restrictions. Tabs, downloads and every other browser feature work
normally, and the client runs unchanged on virtual machines.

Unlike the Windows and macOS SEB clients, which embed a Chromium/WebKit
runtime, this client drives the **system Chromium-based browser** (Chromium,
Google Chrome or Brave) through the
[Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/).
It is written in C#/.NET 8 with **zero NuGet dependencies** in `src/`.

## What is and is not locked

| Area | SEB | exam-unlock |
| --- | --- | --- |
| Config Key / Browser Exam Key request headers | ✅ | ✅ kept |
| SEB user agent (`SEB/<version>`) | ✅ | ✅ kept |
| `window.SafeExamBrowser` JavaScript API | ✅ | ✅ kept (all documents, incl. new tabs) |
| Quit URL (+ quit password), SEB Server mode | ✅ | ✅ kept |
| Request / URL filter (block-by-default) | ✅ | ❌ removed — every request is allowed |
| Kiosk / fullscreen enforcement | ✅ | ❌ removed — normal window |
| Process monitor (terminates applications) | ✅ | ❌ removed |
| Virtual machine detection | ✅ | ❌ removed — runs on any VM |
| Clipboard blocking / isolation | ✅ | ❌ removed |
| Print / page zoom restrictions | ✅ | ❌ removed |
| New tab / window restrictions | ✅ | ❌ removed — tabs work |
| Download blocking | configurable | ❌ removed — downloads always allowed |

## Quick start

Requirements: .NET 8 SDK (build) and Chromium or Google Chrome (runtime).

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

Build binaries for both platforms (self-contained, no .NET runtime needed on
the target machine):

```sh
scripts/build.sh
# -> artifacts/exam-unlock-linux-x64/exam-unlock
# -> artifacts/exam-unlock-win-x64/exam-unlock.exe
```

Install for the current user (Linux): `./scripts/install.sh`
(binary: `~/.local/bin/exam-unlock`).

### CLI

```
exam-unlock <command> <configuration> [options]

Commands:
  run     Start an exam session with the given configuration
  info    Show the settings of the given configuration
  keys    Show the config key and browser exam key of the configuration
  check   Validate the given configuration
  version Show the program version
  help    Show this help

Options:
  --password <password>   The password for an encrypted configuration
  --headless              Launch the browser without a display server (testing)
  --chromium <path>       The Chromium executable to be used
  --url <url>             The URL for which the request hashes are to be shown
  --debug                 Enable debug logging
```

The configuration may be a file path, an `http(s)` URL or a `data:` URI.
The `keys` command prints the values an exam administrator needs for the
configuration of their LMS.

## How exams are verified

While an exam configuration is active, the browser behaves like SEB on
Windows/macOS for everything the exam system checks:

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
Moodle `quizaccess_seb` plugin and upstream unit tests, and end-to-end against
a mock exam server (see `scripts/e2e.sh`), including the values seen in a
**newly opened tab**.

> **Note:** the Browser Exam Key is bound to the SEB build version, exactly as
> with upstream. When the version changes, exam administrators must update the
> key in the LMS. Institutions that want a pinned value can set the environment
> variable `SEB_LINUX_BEK_SIGNATURE` to a stable string.

## Architecture

```
src/
  SafeExamBrowser.Linux.Configuration  Configuration file formats, Config Key / BEK
  SafeExamBrowser.Linux.Browser        Chromium control via DevTools Protocol
  SafeExamBrowser.Linux.Client         Session orchestration
  SafeExamBrowser.Linux.Server         SEB Server (Exam API v1) client
  SafeExamBrowser.Linux.Cli            exam-unlock command line interface
tests/
  SafeExamBrowser.Linux.Tests          Unit tests incl. upstream-ported suites
  e2e/                                 End-to-end harness (mock exam + SEB Server)
```

Curious implementation details:

- The request interception pipeline still rewrites `seb://`/`sebs://` URLs,
  handles the quit URL and detects configuration downloads, but it never
  blocks a request: everything is continued (with the SEB headers appended).
- Because the renderer is paused while a document request is intercepted, the
  SEB JavaScript API layer for the next document is registered *before* the
  request is continued, using fire-and-forget DevTools commands to avoid a
  deadlock (the renderer does not answer while paused).
- New tabs and windows are attached automatically, so the SEB JavaScript API
  and the per-URL key hashes are available there as well.
- With no display server available (or `SEB_HEADLESS=1`), the browser runs
  headless — used by the tests and by CI.

## Running on a VM

No virtual-machine detection is performed. The client behaves identically on
QEMU/KVM, VirtualBox, VMware, Hyper-V and any other virtualization platform,
regardless of the `allowVirtualMachine` setting in the configuration.

## Configuration compatibility

- Supported: plain plist XML, gzip-compressed and binary (`plnd`) SEB
  configurations, password-encrypted (`pswd`/`pwcc`) configurations,
  `seb://` and `sebs://` URL rewriting, configuration download detection.
- **Not supported:** certificate-encrypted configurations (RSA public key with
  password or certificate hash). Loading such a file reports `NotSupported`.
- SEB Server mode is fully implemented (handshake, app signature key, exam
  configuration download, pings and instructions).

## License

Mozilla Public License 2.0 (MPL-2.0), see [LICENSE](LICENSE) and
[NOTICE.md](NOTICE.md). This is an independent community port; "Safe Exam
Browser" is a trademark of ETH Zürich.
