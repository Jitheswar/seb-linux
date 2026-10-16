/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 *
 * Key derivation (see https://safeexambrowser.org/developer/documents/SEB-Specification-BrowserExamKey.pdf):
 *
 *   Config Key hash for URL  = SHA256(urlWithoutFragment + ConfigKey)
 *   Browser Exam Key         = HMAC-SHA256(key = examKeySalt, message = CodeSignatureHash + ProgramBuildVersion + ConfigKey)
 *   Browser Exam Key hash    = SHA256(urlWithoutFragment + BrowserExamKey)
 *
 * All hash values are Base16 (lowercase) encoded. The values sent to exam systems
 * (HTTP header or JS API) are the *hash for URL* values, not the raw keys.
 */

using System;
using System.Security.Cryptography;
using System.Text;
using SafeExamBrowser.Linux.Configuration.Contracts;

namespace SafeExamBrowser.Linux.Configuration.Cryptography
{
	public class KeyGenerator
	{
		private readonly object @lock = new object();

		private readonly ILogger logger;

		private string browserExamKey;

		public KeyGenerator(ILogger logger)
		{
			this.logger = logger;
		}

		public string CalculateBrowserExamKeyHash(string configurationKey, byte[] salt, string url)
		{
			var urlWithoutFragment = url.Split('#')[0];
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(urlWithoutFragment + (browserExamKey ?? ComputeBrowserExamKey(configurationKey, salt))));
			var key = ToString(hash);

			return key;
		}

		public string CalculateConfigurationKeyHash(string configurationKey, string url)
		{
			var urlWithoutFragment = url.Split('#')[0];
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(urlWithoutFragment + configurationKey));
			var key = ToString(hash);

			return key;
		}

		public void UseCustomBrowserExamKey(string browserExamKey)
		{
			if (browserExamKey != default)
			{
				this.browserExamKey = browserExamKey;
				logger.Debug("Initialized custom browser exam key.");
			}
		}

		public string CalculateBrowserExamKey(string configurationKey, byte[] salt)
		{
			return ComputeBrowserExamKey(configurationKey, salt);
		}

		private string ComputeBrowserExamKey(string configurationKey, byte[] salt)
		{
			lock (@lock)
			{
				if (browserExamKey == default)
				{
					logger.Debug("Initializing browser exam key...");

					if (configurationKey == default)
					{
						configurationKey = "";
						logger.Warn("The current configuration does not contain a value for the configuration key!");
					}

					if (salt == default || salt.Length == 0)
					{
						salt = new byte[0];
						logger.Warn("The current configuration does not contain a salt value for the browser exam key!");
					}

					// The Linux port uses the simplified calculation which upstream SEB applies
					// when the Windows integrity module is unavailable.
					using (var algorithm = new HMACSHA256(salt))
					{
						var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(AppInfo.CodeSignatureHash + AppInfo.ProgramBuildVersion + configurationKey));
						var key = ToString(hash);

						browserExamKey = key;
					}

					logger.Debug("Successfully calculated browser exam key using simplified calculation.");
				}
			}

			return browserExamKey;
		}

		private string ToString(byte[] bytes)
		{
			return Convert.ToHexString(bytes).ToLowerInvariant();
		}
	}
}
