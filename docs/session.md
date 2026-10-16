# Session behavior

`exam-unlock` launches the system Chromium-based browser in a **normal
window**. There is no kiosk mode and no session-level lock:

- New tabs and windows can be opened and switched at any time; the SEB
  JavaScript API and the per-URL key hashes are present in every document.
- The full clipboard, printing, page zoom, file uploads and downloads work.
- Any other application can be run in parallel; nothing is monitored or
  terminated.
- No virtual machine detection is performed: the client behaves identically
  on QEMU/KVM, VirtualBox, VMware, Hyper-V and any other virtualization
  platform, regardless of the configuration's `allowVirtualMachine` setting.

The only things the client still enforces are what SEB itself requires for
exam verification: the SEB user agent, the Config Key / Browser Exam Key
request headers and the `window.SafeExamBrowser` JavaScript API in every
document.

The exam ends when the browser window is closed, when the configured quit URL
(with the quit password, if set) is visited, or via a SEB Server instruction.
If a locked-down exam room is wanted (dedicated user, kiosk compositor,
screen capture), that is now entirely up to the exam operator — this client
imposes nothing at the session level.
