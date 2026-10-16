# Kiosk session for safe exams

`seb-linux` launches a Chromium kiosk, but a full exam lockdown also requires
that the **session itself** cannot be escaped: no task switcher, no
notifications, no virtual terminals, no other windows. On Linux this is
achieved with a **kiosk compositor** — a minimal Wayland session that runs
exactly one application: SEB.

## Recommended: cage

[cage](https://github.com/cage-kiosk/cage) is a single-application Wayland
kiosk compositor. Inside cage there is no shell, no window manager and no way
to switch to another application. Run:

```sh
cage -- seb-linux run /path/to/exam.seb
```

For an exam room, create a dedicated user and make the cage command start
automatically at login, e.g. via `~/.profile` for a console autologin:

```sh
# ~/.profile of the exam user
if [ -z "$SEB_STARTED" ] && [ "$(tty)" = "/dev/tty1" ]; then
	export SEB_STARTED=1
	exec cage -- seb-linux run ~/exam.seb
fi
```

and configure the display manager or `agetty` to auto-login the exam user on
tty1. Make sure the user has no shell escape (no terminal emulator installed,
no passwordless `sudo`).

## Alternative: nested or X11 sessions

- **Gamescope** (`gamescope -- seb-linux run exam.seb`) works on top of an
  existing desktop during testing.
- On a plain **X11 session** there are no compositor guarantees; run SEB
  fullscreen (`--no-kiosk` off) and consider disabling the desktop's own
  keyboard shortcuts. The `AllowAltTab`, `AllowCtrlEsc` etc. settings of the
  configuration are enforced by the kiosk compositor / window manager, not by
  SEB itself.

## What SEB enforces itself

- Request filtering (main and content request filter of the configuration)
- Exam keys: `X-SafeExamBrowser-ConfigKeyHash` / `X-SafeExamBrowser-RequestHash`
  headers and the SEB JavaScript API for LMS-side verification
- Clipboard isolation or blocking, page zoom and print restrictions
- Quit URL handling with optional quit password
- Process monitoring: configured applications that are not allowed to run are
  terminated while the session is active (screenshot tools, messengers, ...)

## What the kiosk session must enforce

- No task switcher / overview / notifications (cage: nothing to configure)
- No virtual terminal switching (`chvt`, Ctrl+Alt+F1..): restrict with
  logind `NAutoVTs=1` and a locked-down `X11VTs` / no getty on other VTs
- Screen locking and power management disabled for the exam user
- Non-root: SEB never requires root; do not run it as root

## Screen capture

Under a Wayland kiosk compositor without `xdg-desktop-portal`, screen capture
(clipboard portals, PipeWire screencast) is unavailable to any application,
which is exactly what an exam requires. Verify that
`xdg-desktop-portal-*` services are not reachable in the exam session; the
configuration setting `AllowWindowCapture=false` (default) documents the
requirement, but the enforcement point is the session.
