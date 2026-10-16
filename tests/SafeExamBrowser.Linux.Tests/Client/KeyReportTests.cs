/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SafeExamBrowser.Linux.Client;
using SafeExamBrowser.Linux.Configuration;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Tests
{
	public class KeyReportTests
	{
		private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "testdata");

		private const string ExpectedConfigurationKey = "4fa9af8ec8759eb7c680752ef4ee5eaf1a860628608fccae2715d519849f9292";
		private const string ExpectedBrowserExamKey = "d57306133383cf27675bde29241d215c8adf3a98d32175236897a5c435984ff2";

		private readonly ILogger logger = new ConsoleLogger("tests", false);

		[Fact]
		public void MustReportKeysOfRealConfigurationFile()
		{
			var settings = LoadSettings("seb/unencrypted_mac_001.seb");
			var report = new KeyReport(settings.Browser, logger);

			Assert.Equal(ExpectedConfigurationKey, report.ConfigurationKey);
			Assert.Equal(ExpectedBrowserExamKey, report.BrowserExamKey);
		}

		[Fact]
		public void MustCalculateRequestHashesLikeTheBrowser()
		{
			var settings = LoadSettings("seb/unencrypted_mac_001.seb");
			var report = new KeyReport(settings.Browser, logger);
			var url = "https://moodle.example.org/mod/quiz/view.php?id=42";

			Assert.Equal(Sha256(url + report.ConfigurationKey), report.ConfigurationKeyHash(url));
			Assert.Equal(Sha256(url + report.BrowserExamKey), report.BrowserExamKeyHash(url));
		}

		[Fact]
		public void MustStripUrlFragmentsWhenCalculatingRequestHashes()
		{
			var settings = LoadSettings("seb/unencrypted_mac_001.seb");
			var report = new KeyReport(settings.Browser, logger);

			Assert.Equal(report.ConfigurationKeyHash("https://example.org/"), report.ConfigurationKeyHash("https://example.org/#section"));
			Assert.Equal(report.BrowserExamKeyHash("https://example.org/"), report.BrowserExamKeyHash("https://example.org/#section"));
		}

		[Fact]
		public void MustUseCustomBrowserExamKeyWhenProvided()
		{
			var settings = LoadSettings("seb/unencrypted_mac_001.seb");

			settings.Browser.CustomBrowserExamKey = "custom-browser-exam-key";

			var report = new KeyReport(settings.Browser, logger);

			Assert.Equal("custom-browser-exam-key", report.BrowserExamKey);
			Assert.Equal(Sha256("https://example.org/" + "custom-browser-exam-key"), report.BrowserExamKeyHash("https://example.org/"));
		}

		private AppSettings LoadSettings(string file)
		{
			var repository = new ConfigurationRepository(logger);
			var status = repository.TryLoadSettings(new Uri(Path.Combine(TestData, file)), out var settings);

			Assert.Equal(LoadStatus.Success, status);

			return settings;
		}

		private static string Sha256(string value)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
		}
	}
}
