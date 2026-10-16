# Third-party notices and upstream attribution

## Safe Exam Browser

This project is a **Linux port of Safe Exam Browser** and is derived from the
upstream Safe Exam Browser source code by ETH Zürich, IT Services:

- https://github.com/SafeExamBrowser/seb-win-refactoring (Windows client, C#/.NET)
- https://github.com/SafeExamBrowser/seb-mac (macOS client, Objective-C)
- https://github.com/SafeExamBrowser/moodle-quizaccess_seb (Moodle quiz access rule)

Upstream is licensed under the **Mozilla Public License, Version 2.0** (MPL-2.0).
This repository is likewise licensed under MPL-2.0; see `LICENSE`.

Portions of this codebase (notably the configuration file format handling,
the Config Key and Browser Exam Key algorithms, and selected settings key
names) are direct ports of upstream Safe Exam Browser code and retain the
original copyright notice:

    Copyright (c) 2010-2026 ETH Zürich, IT Services

MPL-2.0 is a file-level copyleft license: files derived from upstream SEB
remain under MPL-2.0 and their source must be made available.

## Open specifications used by this port

- SEB Config Key generation: https://safeexambrowser.org/developer/seb-config-key.html
- SEB Browser Exam Key: https://safeexambrowser.org/developer/documents/SEB-Specification-BrowserExamKey.pdf
- SEB file format: https://safeexambrowser.org/developer/seb-file-format.html
- Chrome DevTools Protocol: https://chromedevtools.github.io/devtools-protocol/

## Trademarks

"Safe Exam Browser" and the SEB logo are trademarks of ETH Zürich. This
project is an independent community port and is not an official ETH Zürich
release unless stated otherwise.
