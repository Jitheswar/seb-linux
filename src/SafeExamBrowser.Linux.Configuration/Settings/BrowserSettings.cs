/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System.Collections.Generic;

namespace SafeExamBrowser.Linux.Configuration.Settings
{
	public enum UrlPolicy
	{
		Never = 0,
		LoadError = 1,
		BeforeTitle = 2,
		Always = 3
	}

	public enum WindowMode
	{
		Windowed = 0,
		Fullscreen = 1
	}

	public class BrowserSettings
	{
		public AdditionalWindowSettings AdditionalWindow { get; } = new AdditionalWindowSettings();
		public MainWindowSettings MainWindow { get; } = new MainWindowSettings();

		public bool AllowConfigurationDownloads { get; set; }
		public bool AllowCustomDownUploadLocation { get; set; }
		public bool AllowDeveloperConsole { get; set; }
		public bool AllowDownloads { get; set; }
		public bool AllowFind { get; set; }
		public bool AllowPageZoom { get; set; }
		public bool AllowPdfReader { get; set; }
		public bool AllowPdfReaderToolbar { get; set; }
		public bool AllowPrint { get; set; }
		public bool AllowSpellChecking { get; set; }
		public bool AllowUploads { get; set; }

		/// <summary>
		/// The Config Key of the currently loaded configuration, calculated from the raw
		/// configuration data (see DataProcessor.CalculateConfigurationKey).
		/// </summary>
		public string ConfigurationKey { get; set; }

		/// <summary>
		/// A custom browser exam key which overrides the value calculated from the configuration.
		/// Used when the browser exam key is provided by a SEB Server (see also <see cref="BrowserExamKeySalt"/>).
		/// </summary>
		public string CustomBrowserExamKey { get; set; }

		public string CustomUserAgent { get; set; }
		public bool DeleteCacheOnShutdown { get; set; }
		public bool DeleteCookiesOnShutdown { get; set; }
		public bool DeleteCookiesOnStartup { get; set; }
		public string DownloadDirectory { get; set; }
		public bool DownloadPdfFiles { get; set; }
		public bool EnableBrowser { get; set; }
		public byte[] BrowserExamKeySalt { get; set; }
		public UrlFilterSettings Filter { get; } = new UrlFilterSettings();
		public string HomeButtonMessage { get; set; }
		public bool HomeButtonRequiresPassword { get; set; }
		public string HomeUrl { get; set; }
		public string HomePasswordHash { get; set; }
		public bool ShowHomeButton { get; set; }
		public string QuitUrl { get; set; }
		public bool QuitUrlConfirmation { get; set; }
		public bool ResetOnQuitUrl { get; set; }
		public bool SendBrowserExamKey { get; set; }
		public bool SendConfigurationKey { get; set; }
		public string StartUrl { get; set; }
		public bool UseIsolatedClipboard { get; set; }
		public bool UseQueryParameter { get; set; }
		public bool UseStartUrlAsHomeUrl { get; set; }
		public bool UseTemporaryDownUploadDirectory { get; set; }
		public string UserAgentSuffix { get; set; }
	}

	public class MainWindowSettings
	{
		public bool AllowAddressBar { get; set; }
		public bool AllowNavigation { get; set; }
		public bool AllowReloading { get; set; }
		public bool ShowHomeButton { get; set; }
		public bool ShowReloadButton { get; set; }
		public bool ShowReloadWarning { get; set; }
		public bool ShowToolbar { get; set; }
		public UrlPolicy UrlPolicy { get; set; }
		public string WindowHeight { get; set; }
		public WindowMode WindowMode { get; set; }
		public string WindowPosition { get; set; }
		public string WindowWidth { get; set; }
	}

	public class AdditionalWindowSettings
	{
		public bool AllowAddressBar { get; set; }
		public bool AllowNavigation { get; set; }
		public bool AllowReloading { get; set; }
		public bool ShowReloadButton { get; set; }
		public bool ShowReloadWarning { get; set; }
		public bool ShowToolbar { get; set; }
		public UrlPolicy UrlPolicy { get; set; }
		public string WindowHeight { get; set; }
		public string WindowPosition { get; set; }
		public string WindowWidth { get; set; }
	}

	public class UrlFilterSettings
	{
		public bool EnableContentRequestFilter { get; set; }
		public bool EnableMainRequestFilter { get; set; }
		public List<UrlFilterRule> Rules { get; } = new List<UrlFilterRule>();
	}

	public class UrlFilterRule
	{
		/// <summary>True for an allow rule, false for a block rule.</summary>
		public bool Action { get; set; }

		public bool IsActive { get; set; }

		public string Expression { get; set; }

		public bool ExpressionIsRegex { get; set; }
	}
}
