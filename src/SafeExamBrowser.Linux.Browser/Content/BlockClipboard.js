/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 *
 * Blocks all clipboard interaction of a web page, neither the system clipboard nor
 * any in-page buffer must be usable (clipboard policy "Block").
 */

(function () {
	function block(e) {
		e.preventDefault();
		e.stopImmediatePropagation();
	}

	document.addEventListener('copy', block, true);
	document.addEventListener('cut', block, true);
	document.addEventListener('paste', block, true);
})();

(function () {
	if (navigator.clipboard) {
		var deny = function () {
			return Promise.reject(new DOMException('Clipboard access is blocked by Safe Exam Browser.', 'NotAllowedError'));
		};

		try {
			for (var property of ['read', 'readText', 'write', 'writeText']) {
				if (navigator.clipboard[property]) {
					Object.defineProperty(navigator.clipboard, property, { value: deny, writable: false, configurable: false });
				}
			}
		} catch (e) {
		}
	}
})();
