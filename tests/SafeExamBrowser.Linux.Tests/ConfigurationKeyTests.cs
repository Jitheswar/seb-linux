/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Tests for the Config Key / Browser Exam Key contract with exam systems.
 *
 * Test vectors:
 *   - TestDictionary{1,2,3}.bin: binary fixtures from the upstream (Windows) unit
 *     tests, together with the Config Keys expected there.
 *   - seb/*.seb: genuine Safe Exam Browser configuration files from the Moodle
 *     quizaccess_seb plugin test suite (tests/fixtures), together with the Config
 *     Keys expected by the plugin's own tests (tests/config_key_test.php).
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SafeExamBrowser.Linux.Configuration;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Cryptography;
using SafeExamBrowser.Linux.Tests.Nrbf;

namespace SafeExamBrowser.Linux.Tests
{
	public class ConfigurationKeyTests
	{
		private static readonly string TestData = Path.Combine(AppContext.BaseDirectory, "testdata");

		private readonly ILogger logger = new ConsoleLogger("tests", false);

		[Fact]
		public void MustCalculateExpectedConfigurationKeysForUpstreamBinaryFixtures()
		{
			AssertConfigurationKey("TestDictionary1.bin", "6063c3351ed1ac878c05072598d5079e30ca763c957d8e04bd45131c08f88d1a");
			AssertConfigurationKey("TestDictionary2.bin", "4fc002d2ae4faf994a14bede54d95ac58a1a2cb9b59bc5b4277ff29559b46e3d");
			AssertConfigurationKey("TestDictionary3.bin", "ab426e25b795c917f1fb40f7ef8e5757ef97d7c7ad6792e655c4421d47329d7a");
		}

		[Fact]
		public void MustDecodeUpstreamBinaryFixtures()
		{
			using (var stream = File.OpenRead(Path.Combine(TestData, "TestDictionary1.bin")))
			{
				var rawData = NrbfReader.DeserializeDictionary(stream);
				var rules = rawData["URLFilterRules"] as IList<object>;
				var rule = rules[0] as IDictionary<string, object>;

				Assert.Equal(160, rawData.Count);
				Assert.Equal("SEB_OSX_2.1.2_27A8", rawData["originatorVersion"]);
				Assert.True((bool)rawData["URLFilterEnable"]);
				Assert.Equal(120000, rawData["browserMessagingPingTime"]);
				Assert.Equal("https://moodle-app2.let.ethz.ch/mod/quiz/view.php?id=31480", rawData["startURL"]);
				Assert.IsType<byte[]>(rawData["examKeySalt"]);
				Assert.Equal(32, ((byte[])rawData["examKeySalt"]).Length);
				Assert.Equal(1, rule["action"]);
				Assert.IsType<string>(rule["expression"]);
			}
		}

		[Theory]
		[InlineData("seb/unencrypted_mac_001.seb", "4fa9af8ec8759eb7c680752ef4ee5eaf1a860628608fccae2715d519849f9292")]
		[InlineData("seb/unencrypted_win_223.seb", "fc6f4ea5922717760f4d6d536c23b8d19bf20b52aa97940f5427a76e20f49026")]
		public void MustReproduceMoodleConfigKeyForRealConfigurationFiles(string file, string expected)
		{
			var repository = new ConfigurationRepository(logger);
			var status = repository.TryLoadSettings(new Uri(Path.Combine(TestData, file)), out var settings);

			Assert.Equal(LoadStatus.Success, status);
			Assert.Equal(expected, settings.Browser.ConfigurationKey);
		}

		[Fact]
		public void MustIgnoreOriginatorVersionForConfigurationKey()
		{
			var repository = new ConfigurationRepository(logger);

			Assert.Equal(LoadStatus.Success, repository.TryLoadSettings(new Uri(Path.Combine(TestData, "seb/simpleunencrypted.seb")), out var withVersion));
			Assert.Equal(LoadStatus.Success, repository.TryLoadSettings(new Uri(Path.Combine(TestData, "seb/simpleunencryptedwithoutoriginator.seb")), out var withoutVersion));

			Assert.Equal(withoutVersion.Browser.ConfigurationKey, withVersion.Browser.ConfigurationKey);
		}

		[Fact]
		public void MustCalculateConfigurationKeyHashForUrl()
		{
			var keyGenerator = new KeyGenerator(logger);

			Assert.Equal(
				"c078d6dc816631173b174bd997feea3d903012c576b881761cc351e9cb61fa64",
				keyGenerator.CalculateConfigurationKeyHash("abc", "https://example.com/mod/quiz/view.php?id=42"));
		}

		[Fact]
		public void MustIgnoreUrlFragmentsForKeyHashes()
		{
			var keyGenerator = new KeyGenerator(logger);
			var url = "https://example.com/mod/quiz/view.php?id=42";

			Assert.Equal(
				keyGenerator.CalculateConfigurationKeyHash("abc", url),
				keyGenerator.CalculateConfigurationKeyHash("abc", $"{url}#section-2"));
		}

		[Fact]
		public void MustCalculateBrowserExamKey()
		{
			var salt = Convert.FromHexString("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");
			var keyGenerator = new KeyGenerator(logger);

			if (Environment.GetEnvironmentVariable("SEB_LINUX_BEK_SIGNATURE") == null)
			{
				Assert.Equal("safe-exam-browser-linux", AppInfo.CodeSignatureHash);
				Assert.Equal("3.10.2", AppInfo.ProgramBuildVersion);
			}

			Assert.Equal(
				"10144b5c202cdf624009647eedbd5fb345fb1a3b64e03acbdc24c29056b107f7",
				keyGenerator.CalculateBrowserExamKey("abc", salt));
		}

		[Fact]
		public void MustCalculateBrowserExamKeyHashForUrl()
		{
			var salt = Convert.FromHexString("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff");
			var keyGenerator = new KeyGenerator(logger);

			Assert.Equal(
				"a1328ae511ca5f28b3acf5b9aded576ce745b4a8278406c309ea35b7dd963fab",
				keyGenerator.CalculateBrowserExamKeyHash("abc", salt, "https://example.com/mod/quiz/view.php?id=42"));
		}

		[Fact]
		public void MustRequestPasswordForDecryption()
		{
			var encryption = new PasswordEncryption(logger);

			Assert.Equal(LoadStatus.PasswordNeeded, encryption.Decrypt(new MemoryStream(new byte[128]), null, out _));
		}

		[Fact]
		public void MustFailDecryptionWithWrongPassword()
		{
			var encryption = new PasswordEncryption(logger);

			Assert.Equal(SaveStatus.Success, encryption.Encrypt(new MemoryStream(Encoding.UTF8.GetBytes("A super secret message!")), "test1234", out var encrypted));
			Assert.Equal(LoadStatus.PasswordNeeded, encryption.Decrypt(encrypted, "not the correct password", out _));
		}

		[Fact]
		public void MustRoundTripPasswordEncryption()
		{
			var message = Encoding.UTF8.GetBytes("A super secret message!");
			var encryption = new PasswordEncryption(logger);

			Assert.Equal(SaveStatus.Success, encryption.Encrypt(new MemoryStream(message), "test1234", out var encrypted));
			Assert.Equal(LoadStatus.Success, encryption.Decrypt(encrypted, "test1234", out var decrypted));

			decrypted.Seek(0, SeekOrigin.Begin);

			Assert.Equal(message, ReadAll(decrypted));
		}

		[Fact]
		public void MustSaveAndReloadConfigurationPreservingTheConfigurationKey()
		{
			var rawData = CreateSampleConfiguration();
			var expected = ConfigurationRepository.CalculateConfigurationKey(rawData);
			var path = TemporaryFilePath();

			try
			{
				var repository = new ConfigurationRepository(logger);

				Assert.Equal(SaveStatus.Success, repository.SaveConfiguration(rawData, path));
				Assert.Equal(LoadStatus.Success, repository.TryLoadSettings(new Uri(path), out var settings));

				Assert.Equal("https://example.com/mod/quiz/view.php?id=42", settings.Browser.StartUrl);
				Assert.Equal("600", settings.Browser.MainWindow.WindowWidth);
				Assert.Equal(expected, settings.Browser.ConfigurationKey);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Fact]
		public void MustSaveAndReloadPasswordProtectedConfiguration()
		{
			var rawData = CreateSampleConfiguration();
			var password = new PasswordParameters { Password = "test1234" };
			var path = TemporaryFilePath();

			try
			{
				var repository = new ConfigurationRepository(logger);

				Assert.Equal(SaveStatus.Success, repository.SaveConfiguration(rawData, path, password));
				Assert.Equal(LoadStatus.PasswordNeeded, repository.TryLoadSettings(new Uri(path), out _));
				Assert.Equal(LoadStatus.PasswordNeeded, repository.TryLoadSettings(new Uri(path), out _, new PasswordParameters { Password = "wrong password" }));
				Assert.Equal(LoadStatus.Success, repository.TryLoadSettings(new Uri(path), out var settings, password));

				Assert.Equal("https://example.com/mod/quiz/view.php?id=42", settings.Browser.StartUrl);
				Assert.Equal(ConfigurationRepository.CalculateConfigurationKey(rawData), settings.Browser.ConfigurationKey);
			}
			finally
			{
				File.Delete(path);
			}
		}

		private void AssertConfigurationKey(string file, string expected)
		{
			using (var stream = File.OpenRead(Path.Combine(TestData, file)))
			{
				var rawData = NrbfReader.DeserializeDictionary(stream);

				Assert.Equal(expected, ConfigurationRepository.CalculateConfigurationKey(rawData));
			}
		}

		private Dictionary<string, object> CreateSampleConfiguration()
		{
			return new Dictionary<string, object>
			{
				["originatorVersion"] = "SEB_LINUX_0.1.0",
				["startURL"] = "https://example.com/mod/quiz/view.php?id=42",
				["allowQuit"] = true,
				["showTaskBar"] = false,
				["browserViewMode"] = 1,
				["mainBrowserWindowWidth"] = "600",
				["examKeySalt"] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
				["URLFilterRules"] = new List<object>
				{
					new Dictionary<string, object>
					{
						["action"] = 1,
						["active"] = true,
						["expression"] = "https://example.com/.*",
						["regex"] = true
					}
				},
				["embeddedCertificates"] = new List<object>()
			};
		}

		private byte[] ReadAll(Stream stream)
		{
			using (var buffer = new MemoryStream())
			{
				stream.CopyTo(buffer);

				return buffer.ToArray();
			}
		}

		private string TemporaryFilePath()
		{
			return Path.Combine(Path.GetTempPath(), $"seb-linux-test-{Guid.NewGuid():N}.seb");
		}
	}
}
