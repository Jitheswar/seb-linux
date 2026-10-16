/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

namespace SafeExamBrowser.Linux.Configuration.Settings
{
	public enum ClipboardPolicy
	{
		/// <summary>
		/// Allows the usage of the system clipboard without restrictions.
		/// </summary>
		Allow,

		/// <summary>
		/// Completely blocks the usage of the system clipboard and all related keyboard shortcuts.
		/// </summary>
		Block,

		/// <summary>
		/// Allows the usage of a private clipboard which is reset on exam start and not shared with the system.
		/// </summary>
		Isolated
	}

	public class SecuritySettings
	{
		public string AdminPasswordHash { get; set; }
		public bool AllowApplicationLog { get; set; }
		public bool AllowReconfiguration { get; set; }
		public bool AllowTermination { get; set; }
		public bool AllowVirtualMachine { get; set; }
		public bool AllowWindowCapture { get; set; }
		public ClipboardPolicy ClipboardPolicy { get; set; }
		public bool DisableSessionChangeLockScreen { get; set; }
		public string QuitPasswordHash { get; set; }
		public string ReconfigurationUrl { get; set; }
		public bool VerifySessionIntegrity { get; set; }
		public string VersionRestrictions { get; set; }
	}

	public class KeyboardSettings
	{
		public bool AllowAltEsc { get; set; }
		public bool AllowAltF4 { get; set; }
		public bool AllowAltTab { get; set; }
		public bool AllowCtrlC { get; set; }
		public bool AllowCtrlEsc { get; set; }
		public bool AllowCtrlV { get; set; }
		public bool AllowCtrlX { get; set; }
		public bool AllowEsc { get; set; }
		public bool[] AllowFunctionKeys { get; } = new bool[13];
		public bool AllowPrintScreen { get; set; }
		public bool AllowSystemKey { get; set; }
	}

	public class MouseSettings
	{
		public bool AllowMiddleMouseButton { get; set; }
		public bool AllowRightMouseButton { get; set; }
	}
}
