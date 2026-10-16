/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see Tools/KeyGenerator on Windows).
 *
 * Key derivation (see https://safeexambrowser.org/developer/documents/SEB-Specification-BrowserExamKey.pdf):
 *
 *   Config Key hash for URL  = SHA256(urlWithoutFragment + ConfigKey)
 *   Browser Exam Key         = HMAC-SHA256(key = examKeySalt, message = CodeSignatureHash + ProgramBuildVersion + ConfigKey)
 *   Browser Exam Key hash    = SHA256(urlWithoutFragment + BrowserExamKey)
 */

using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Cryptography;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Client
{
	/// <summary>
	/// Provides the configuration keys of a loaded configuration for display, e.g. for exam
	/// administrators who need to manually enter them into their examination system.
	/// </summary>
	public sealed class KeyReport
	{
		private readonly KeyGenerator keyGenerator;
		private readonly BrowserSettings settings;

		public KeyReport(BrowserSettings settings, ILogger logger)
		{
			this.settings = settings;
			keyGenerator = new KeyGenerator(logger);
			keyGenerator.UseCustomBrowserExamKey(settings?.CustomBrowserExamKey);

			ConfigurationKey = settings?.ConfigurationKey;
			BrowserExamKey = settings == default ? default : keyGenerator.CalculateBrowserExamKey(settings.ConfigurationKey, settings.BrowserExamKeySalt);
		}

		/// <summary>
		/// The config key of the loaded configuration.
		/// </summary>
		public string ConfigurationKey { get; }

		/// <summary>
		/// The browser exam key calculated from the configuration (or the custom key, if provided).
		/// </summary>
		public string BrowserExamKey { get; }

		/// <summary>
		/// The configuration key hash to be sent to the given URL (X-SafeExamBrowser-ConfigKeyHash).
		/// </summary>
		public string ConfigurationKeyHash(string url)
		{
			return keyGenerator.CalculateConfigurationKeyHash(ConfigurationKey, url);
		}

		/// <summary>
		/// The browser exam key hash to be sent to the given URL (X-SafeExamBrowser-RequestHash).
		/// </summary>
		public string BrowserExamKeyHash(string url)
		{
			return keyGenerator.CalculateBrowserExamKeyHash(ConfigurationKey, settings?.BrowserExamKeySalt, url);
		}
	}
}
