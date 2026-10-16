/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Security.Cryptography;
using System.Text;
using SafeExamBrowser.Linux.Client;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Tests
{
	public class ConfigurationSummaryTests
	{
		[Fact]
		public void MustDescribeRelevantSettings()
		{
			var settings = new AppSettings();

			settings.Browser.StartUrl = "https://exam.example.org/";
			settings.Browser.QuitUrl = "https://exam.example.org/quit";
			settings.Browser.SendBrowserExamKey = true;
			settings.Browser.SendConfigurationKey = true;
			settings.Browser.BrowserExamKeySalt = new byte[32];
			settings.Browser.Filter.EnableMainRequestFilter = true;
			settings.Browser.Filter.Rules.Add(new UrlFilterRule { Action = true, Expression = "exam.example.org", IsActive = true });
			settings.Browser.Filter.Rules.Add(new UrlFilterRule { Action = false, Expression = ".*\\.txt", IsActive = false, ExpressionIsRegex = true });
			settings.Security.ClipboardPolicy = ClipboardPolicy.Isolated;
			settings.Security.QuitPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("secret")));

			var text = Normalize(string.Join("\n", ConfigurationSummary.Describe(settings)));

			Assert.Contains("Start URL: https://exam.example.org/", text);
			Assert.Contains("Quit URL: https://exam.example.org/quit", text);
			Assert.Contains("Send exam key: true", text);
			Assert.Contains("Exam key salt: 32 bytes", text);
			Assert.Contains("Main request filter: enabled", text);
			Assert.Contains("Rules: 2", text);
			Assert.Contains("- allow simplified: exam.example.org", text);
			Assert.Contains("- block regex: .*\\.txt, inactive", text);
			Assert.Contains("Clipboard: Isolated", text);
			Assert.Contains("Quit password: set", text);
			Assert.Contains("Admin password: not set", text);
		}

		[Fact]
		public void MustDescribeEmptySettings()
		{
			var text = Normalize(string.Join("\n", ConfigurationSummary.Describe(new AppSettings())));

			Assert.Contains("Start URL: none", text);
			Assert.Contains("Quit URL: none", text);
			Assert.Contains("Configuration key: none", text);
			Assert.Contains("Clipboard: Allow", text);
		}

		private static string Normalize(string text)
		{
			return System.Text.RegularExpressions.Regex.Replace(text, "[ ]+", " ");
		}
	}
}
