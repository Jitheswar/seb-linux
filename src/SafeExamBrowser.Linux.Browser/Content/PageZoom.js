/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 * Extended for the Linux port: keyboard shortcuts for page zoom are suppressed
 * directly within the page, as no native keyboard interception is available.
 */

(function () {
	function disableMouseWheelZoom(e) {
		if (e.ctrlKey) {
			e.preventDefault();
			e.stopPropagation();
		}
	}

	function disableZoomKeys(e) {
		var zoomKey = e.key === '+' || e.key === '=' || e.key === '-' || e.key === '0' || e.key === 'Add' || e.key === 'Subtract';

		if ((e.ctrlKey || e.metaKey) && !e.altKey && zoomKey) {
			e.preventDefault();
			e.stopPropagation();
		}
	}

	document.addEventListener('wheel', disableMouseWheelZoom, { passive: false });
	window.addEventListener('keydown', disableZoomKeys, true);
	window.addEventListener('keyup', disableZoomKeys, true);
})();
