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
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Tests
{
	public class ConfigurationLoaderTests
	{
		private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "testdata");

		private const string ExpectedConfigurationKey = "4fa9af8ec8759eb7c680752ef4ee5eaf1a860628608fccae2715d519849f9292";

		private readonly ILogger logger = new ConsoleLogger("tests", false);

		[Fact]
		public void MustLoadRealConfigurationFileByPath()
		{
			var result = new ConfigurationLoader(logger).Load(Path.Combine(TestData, "seb/unencrypted_mac_001.seb"));

			Assert.Equal(LoadStatus.Success, result.Status);
			Assert.Equal(ExpectedConfigurationKey, result.Settings.Browser.ConfigurationKey);
		}

		[Fact]
		public void MustLoadRealConfigurationFileByFileUri()
		{
			var path = Path.Combine(TestData, "seb/unencrypted_mac_001.seb");
			var result = new ConfigurationLoader(logger).Load(new Uri(path).AbsoluteUri);

			Assert.Equal(LoadStatus.Success, result.Status);
			Assert.Equal(ExpectedConfigurationKey, result.Settings.Browser.ConfigurationKey);
		}

		[Fact]
		public void MustCalculateConfigurationKeyWithoutLoadingSettings()
		{
			var key = new ConfigurationLoader(logger).CalculateConfigurationKey(Path.Combine(TestData, "seb/unencrypted_mac_001.seb"));

			Assert.Equal(ExpectedConfigurationKey, key);
		}

		[Fact]
		public void MustReportInvalidDataForMissingFile()
		{
			var result = new ConfigurationLoader(logger).Load(Path.Combine(TestData, "seb/does-not-exist.seb"));

			Assert.Equal(LoadStatus.InvalidData, result.Status);
		}

		[Fact]
		public void MustRequirePasswordForEncryptedConfiguration()
		{
			var encryptedFile = Path.Combine(Path.GetTempPath(), $"seb-test-{Guid.NewGuid():N}.seb");

			try
			{
				CreateEncryptedConfiguration(encryptedFile, "test-password");

				var loader = new ConfigurationLoader(logger);
				var withoutPassword = loader.Load(encryptedFile);
				var withWrongPassword = loader.Load(encryptedFile, "wrong-password");
				var withPassword = loader.Load(encryptedFile, "test-password");

				Assert.Equal(LoadStatus.PasswordNeeded, withoutPassword.Status);
				Assert.NotEqual(LoadStatus.Success, withWrongPassword.Status);
				Assert.Equal(LoadStatus.Success, withPassword.Status);
				Assert.Equal(ExpectedConfigurationKey, withPassword.Settings.Browser.ConfigurationKey);
			}
			finally
			{
				File.Delete(encryptedFile);
			}
		}

		private void CreateEncryptedConfiguration(string destination, string password)
		{
			var repository = new SafeExamBrowser.Linux.Configuration.ConfigurationRepository(logger);
			var source = new Uri(Path.Combine(TestData, "seb/unencrypted_mac_001.seb"));

			Assert.Equal(LoadStatus.Success, repository.TryLoadData(source, out var data));

			using (data)
			{
				Assert.Equal(LoadStatus.Success, repository.TryParseData(data, out _, out _, out var rawData));
				Assert.Equal(SafeExamBrowser.Linux.Configuration.Contracts.SaveStatus.Success, repository.SaveConfiguration(rawData, destination, new PasswordParameters { Password = password }));
			}
		}

		private static string Sha256(string value)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
		}
	}
}
