/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Client on Windows).
 */

using System.Collections.Generic;
using System.Globalization;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Client
{
	/// <summary>
	/// Renders a human-readable summary of the relevant settings of a loaded configuration.
	/// </summary>
	public static class ConfigurationSummary
	{
		public static List<string> Describe(AppSettings settings)
		{
			var browser = settings.Browser;
			var lines = new List<string>
			{
				"Browser",
				$"  Start URL:           {ValueOrNone(browser.StartUrl)}",
				$"  Home URL:            {ValueOrNone(browser.HomeUrl)}",
				$"  Quit URL:            {ValueOrNone(browser.QuitUrl)}",
				$"  Quit URL confirms:   {YesNo(browser.QuitUrlConfirmation)}",
				$"  Reset on quit URL:   {YesNo(browser.ResetOnQuitUrl)}",
				$"  Window mode:         {browser.MainWindow.WindowMode}",
				$"  User agent suffix:   {ValueOrNone(browser.UserAgentSuffix)}",
				$"  Custom user agent:   {ValueOrNone(browser.CustomUserAgent)}",
				$"  Custom exam key:     {YesNo(!string.IsNullOrWhiteSpace(browser.CustomBrowserExamKey))}",
				$"  Configuration key:   {ValueOrNone(browser.ConfigurationKey)}",
				string.Empty,
				"Exam keys",
				$"  Send config key:     {YesNo(browser.SendConfigurationKey)}",
				$"  Send exam key:       {YesNo(browser.SendBrowserExamKey)}",
				$"  Exam key salt:       {(browser.BrowserExamKeySalt == null || browser.BrowserExamKeySalt.Length == 0 ? "none" : $"{browser.BrowserExamKeySalt.Length} bytes")}",
				string.Empty,
				"Filter",
				$"  Main request filter:    {Enabled(browser.Filter.EnableMainRequestFilter)}",
				$"  Content request filter: {Enabled(browser.Filter.EnableContentRequestFilter)}",
				$"  Rules:                  {browser.Filter.Rules.Count}"
			};

			foreach (var rule in browser.Filter.Rules)
			{
				var action = rule.Action ? "allow" : "block";
				var type = rule.ExpressionIsRegex ? "regex" : "simplified";
				var state = rule.IsActive ? string.Empty : ", inactive";

				lines.Add($"    - {action} {type}: {rule.Expression}{state}");
			}

			lines.AddRange(new[]
			{
				string.Empty,
				"Features",
				$"  Clipboard:           {settings.Security.ClipboardPolicy}",
				$"  Downloads:           {Enabled(browser.AllowDownloads)}",
				$"  Uploads:             {Enabled(browser.AllowUploads)}",
				$"  PDF reader:          {Enabled(browser.AllowPdfReader)} (toolbar: {Enabled(browser.AllowPdfReaderToolbar)})",
				$"  Printing:            {Enabled(browser.AllowPrint)}",
				$"  Page zoom:           {Enabled(browser.AllowPageZoom)}",
				$"  Developer console:   {Enabled(browser.AllowDeveloperConsole)}",
				$"  Configuration file downloads: {Enabled(browser.AllowConfigurationDownloads)}",
				string.Empty,
				"Security",
				$"  Quit password:       {Configured(settings.Security.QuitPasswordHash)}",
				$"  Admin password:      {Configured(settings.Security.AdminPasswordHash)}",
				$"  Allow termination:   {Enabled(settings.Security.AllowTermination)}",
				$"  Reconfiguration:     {Enabled(settings.Security.AllowReconfiguration)}",
				$"  Session integrity:   {Enabled(settings.Security.VerifySessionIntegrity)}",
				$"  Virtual machines:    {Enabled(settings.Security.AllowVirtualMachine)}",
				$"  Screen capture:      {Enabled(settings.Security.AllowWindowCapture)}"
			});

			if (settings.ConfigurationFile.SessionMode == SessionMode.Server || !string.IsNullOrWhiteSpace(settings.Server.ServerUrl))
			{
				lines.AddRange(new[]
				{
					string.Empty,
					"SEB Server",
					$"  Server URL:          {ValueOrNone(settings.Server.ServerUrl)}",
					$"  Exam ID:             {ValueOrNone(settings.Server.ExamId)}"
				});
			}

			return lines;
		}

		private static string Configured(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? "not set" : "set";
		}

		private static string Enabled(bool value)
		{
			return value ? "enabled" : "disabled";
		}

		private static string ValueOrNone(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? "none" : value;
		}

		private static string YesNo(bool value)
		{
			return value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
		}
	}
}
