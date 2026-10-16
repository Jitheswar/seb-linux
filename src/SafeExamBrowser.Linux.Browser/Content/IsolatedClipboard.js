/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Original code taken and adapted from https://github.com/eqsoft/seb2/blob/master/browser/app/modules/SebBrowser.jsm#L1215.
 * Adapted for the Linux port: the isolated clipboard is kept within the exam session
 * (shared between same-origin frames via local storage) instead of the native clipboard,
 * the underlying system clipboard is never used.
 */

(function () {
	if (typeof SafeExamBrowser === 'undefined') {
		return;
	}

	if (typeof SafeExamBrowser.clipboard === 'undefined') {
		SafeExamBrowser.clipboard = {
			id: crypto.randomUUID(),
			ranges: [],
			text: "",

			clear: function () {
				this.ranges = [];
				this.text = "";
			},

			getContentEncoded: function () {
				var bytes = new TextEncoder().encode(this.text);
				var base64 = btoa(String.fromCodePoint(...bytes));

				return base64;
			},

			update: function (id, base64) {
				if (this.id != id) {
					var bytes = Uint8Array.from(atob(base64), (m) => m.codePointAt(0));
					var content = new TextDecoder().decode(bytes);

					this.ranges = [];
					this.text = content;
				}
			}
		};

		// Share copied content between frames of the same origin without ever touching
		// the system clipboard. The token is unique per copy so that repeated copies
		// within the same frame are propagated as well.
		SafeExamBrowser.clipboard.token = null;
		SafeExamBrowser.clipboard.storageKey = 'SafeExamBrowser.clipboard';

		SafeExamBrowser.clipboard.publish = function () {
			try {
				var payload = JSON.stringify({
					token: crypto.randomUUID(),
					content: this.getContentEncoded(),
					source: this.id
				});

				window.localStorage.setItem(this.storageKey, payload);
			} catch (e) {
				// Local storage may be unavailable (e.g. opaque origin), then the clipboard
				// remains usable within the current frame only.
			}
		};

		SafeExamBrowser.clipboard.synchronize = function () {
			try {
				var payload = window.localStorage.getItem(this.storageKey);

				if (payload) {
					var data = JSON.parse(payload);

					if (data && data.token && data.token !== this.token) {
						this.token = data.token;
						this.update(data.source, data.content);
					}
				}
			} catch (e) {
			}
		};

		window.addEventListener('storage', (e) => {
			if (e.key === SafeExamBrowser.clipboard.storageKey) {
				SafeExamBrowser.clipboard.synchronize();
			}
		}, false);
	}

	if (typeof copySelection === 'undefined') {
		// eslint-disable-next-line no-unused-vars
		function copySelection(e) {
			if (e.target.contentEditable && e.target.setRangeText) {
				SafeExamBrowser.clipboard.text = e.target.value.substring(e.target.selectionStart, e.target.selectionEnd);
				SafeExamBrowser.clipboard.ranges = [];
			} else {
				var selection = e.target.ownerDocument.defaultView.getSelection();
				var text = "";

				for (var i = 0; i < selection.rangeCount; i++) {
					SafeExamBrowser.clipboard.ranges[i] = selection.getRangeAt(i).cloneContents();
					text += SafeExamBrowser.clipboard.ranges[i].textContent;
				}

				SafeExamBrowser.clipboard.text = text;
			}
		}
	}

	if (typeof cutSelection === 'undefined') {
		// eslint-disable-next-line no-unused-vars
		function cutSelection(e) {
			if (e.target.contentEditable && e.target.setRangeText) {
				e.target.setRangeText("", e.target.selectionStart, e.target.selectionEnd, 'select');
			} else {
				var designMode = e.target.ownerDocument.designMode;
				var contentEditables = e.target.ownerDocument.querySelectorAll('*[contenteditable]');
				var selection = e.target.ownerDocument.defaultView.getSelection();

				for (var i = 0; i < selection.rangeCount; i++) {
					var range = selection.getRangeAt(i);

					if (designMode === 'on') {
						range.deleteContents();
					} else {
						if (contentEditables.length) {
							contentEditables.forEach(node => {
								if (node.contains(range.commonAncestorContainer)) {
									range.deleteContents();
								}
							});
						}
					}
				}
			}
		}
	}

	if (typeof pasteContent === 'undefined') {
		// eslint-disable-next-line no-unused-vars
		function pasteContent(e) {
			if (e.target.contentEditable && e.target.setRangeText) {
				e.target.setRangeText(SafeExamBrowser.clipboard.text, e.target.selectionStart, e.target.selectionEnd, 'end');
			} else {
				var targetWindow = e.target.ownerDocument.defaultView;
				var designMode = e.target.ownerDocument.designMode;
				var contentEditables = e.target.ownerDocument.querySelectorAll('*[contenteditable]');
				var selection = targetWindow.getSelection();

				for (var i = 0; i < selection.rangeCount; i++) {
					var r = selection.getRangeAt(i);

					if (designMode === 'on') {
						r.deleteContents();
					} else {
						if (contentEditables.length) {
							contentEditables.forEach(node => {
								if (node.contains(r.commonAncestorContainer)) {
									r.deleteContents();
								}
							});
						}
					}
				}

				if (designMode === 'on') {
					var range = targetWindow.getSelection().getRangeAt(0);

					if (SafeExamBrowser.clipboard.ranges.length > 0) {
						SafeExamBrowser.clipboard.ranges.map(r => {
							range = targetWindow.getSelection().getRangeAt(0);
							range.collapse();
							const newNode = r.cloneNode(true);
							range.insertNode(newNode);
							range.collapse();
						});
					} else {
						range.collapse();
						range.insertNode(targetWindow.document.createTextNode(SafeExamBrowser.clipboard.text));
						range.collapse();
					}
				} else {
					if (contentEditables.length) {
						contentEditables.forEach(node => {
							var range = targetWindow.getSelection().getRangeAt(0);

							if (node.contains(range.commonAncestorContainer)) {
								if (SafeExamBrowser.clipboard.ranges.length > 0) {
									SafeExamBrowser.clipboard.ranges.map(r => {
										range = targetWindow.getSelection().getRangeAt(0);
										range.collapse();
										const newNode = r.cloneNode(true);
										range.insertNode(newNode);
										range.collapse();
									});
								} else {
									range = targetWindow.getSelection().getRangeAt(0);
									range.collapse();
									range.insertNode(targetWindow.document.createTextNode(SafeExamBrowser.clipboard.text));
									range.collapse();
								}
							}
						});
					}
				}
			}
		}
	}

	if (typeof notifyInput === 'undefined') {
		// Programmatic value changes (setRangeText/insertNode) emit no input event; notify frameworks explicitly.
		// eslint-disable-next-line no-unused-vars
		function notifyInput(target, inputType) {
			if (target && typeof target.dispatchEvent === 'function') {
				target.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: inputType }));
			}
		}
	}

	if (typeof onCopy === 'undefined') {
		// eslint-disable-next-line no-unused-vars
		function onCopy(e) {
			try {
				SafeExamBrowser.clipboard.clear();

				copySelection(e);
				SafeExamBrowser.clipboard.publish();
			} finally {
				e.preventDefault();
			}

			return false;
		}

		window.document.addEventListener("copy", onCopy, true);
	}

	if (typeof onCut === 'undefined') {
		// eslint-disable-next-line no-unused-vars
		function onCut(e) {
			try {
				SafeExamBrowser.clipboard.clear();

				copySelection(e);
				cutSelection(e);
				SafeExamBrowser.clipboard.publish();

				notifyInput(e.target, "deleteByCut");
			} finally {
				e.preventDefault();
			}

			return false;
		}

		window.document.addEventListener("cut", onCut, true);
	}

	if (typeof onPaste === 'undefined') {
		// eslint-disable-next-line no-unused-vars
		function onPaste(e) {
			try {
				SafeExamBrowser.clipboard.synchronize();
				pasteContent(e);

				notifyInput(e.target, "insertFromPaste");
			} finally {
				e.preventDefault();
			}

			return false;
		}

		window.document.addEventListener("paste", onPaste, true);
	}
})();
