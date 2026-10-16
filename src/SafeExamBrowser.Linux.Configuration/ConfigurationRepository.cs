/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using SafeExamBrowser.Linux.Configuration.ConfigurationData;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Cryptography;
using SafeExamBrowser.Linux.Configuration.DataCompression;
using SafeExamBrowser.Linux.Configuration.DataFormats;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Configuration
{
	/// <summary>
	/// Loads and saves SEB configuration files (.seb): plain plist XML, gzip-compressed,
	/// binary blocks ("plnd") and password-protected configurations ("pswd"/"pwcc").
	/// Port of SafeExamBrowser.Configuration.ConfigurationRepository.
	/// </summary>
	public class ConfigurationRepository
	{
		private readonly BinaryParser binaryParser;
		private readonly BinarySerializer binarySerializer;
		private readonly DataMapper dataMapper;
		private readonly DataProcessor dataProcessor;
		private readonly DataValidator dataValidator;
		private readonly DataValues dataValues;
		private readonly ILogger logger;
		private readonly XmlParser xmlParser;

		public ConfigurationRepository(ILogger logger)
		{
			this.logger = logger;

			var compressor = new GZipCompressor(logger);
			var hashAlgorithm = new HashAlgorithm();
			var passwordEncryption = new PasswordEncryption(logger);
			var xmlSerializer = new XmlSerializer(logger);

			binaryParser = new BinaryParser(compressor, hashAlgorithm, logger, passwordEncryption, new XmlParser(compressor, logger));
			binarySerializer = new BinarySerializer(compressor, logger, passwordEncryption, xmlSerializer);
			dataMapper = new DataMapper();
			dataProcessor = new DataProcessor();
			dataValidator = new DataValidator(logger);
			dataValues = new DataValues();
			xmlParser = new XmlParser(compressor, logger);
		}

		public AppSettings LoadDefaultSettings()
		{
			return dataValues.LoadDefaultSettings();
		}

		/// <summary>
		/// Calculates the SEB Config Key for the given raw configuration data.
		/// See https://safeexambrowser.org/developer/seb-config-key.html
		/// </summary>
		public static string CalculateConfigurationKey(IDictionary<string, object> rawData)
		{
			var settings = new AppSettings();

			new DataProcessor().Process(rawData, settings);

			return settings.Browser.ConfigurationKey;
		}

		public LoadStatus TryLoadSettings(Uri resource, out AppSettings settings, PasswordParameters password = null)
		{
			var status = default(LoadStatus);

			settings = LoadDefaultSettings();
			dataProcessor.ProcessDefault(settings);

			logger.Info($"Initialized default settings, now attempting to load '{resource}'...");

			try
			{
				status = TryLoadData(resource, out var stream);

				using (stream)
				{
					if (status == LoadStatus.Success)
					{
						status = ParseSettings(stream, settings, password);
					}
				}
			}
			catch (Exception e)
			{
				status = LoadStatus.UnexpectedError;
				logger.Error($"Unexpected error while trying to load '{resource}'!", e);
			}

			return status;
		}

		/// <summary>
		/// Loads the settings from already available configuration data, e.g. an exam
		/// configuration downloaded from a SEB Server instance.
		/// </summary>
		public LoadStatus TryLoadSettings(Stream data, out AppSettings settings, PasswordParameters password = null)
		{
			var status = default(LoadStatus);

			settings = LoadDefaultSettings();
			dataProcessor.ProcessDefault(settings);

			try
			{
				status = ParseSettings(data, settings, password);
			}
			catch (Exception e)
			{
				status = LoadStatus.UnexpectedError;
				logger.Error("Unexpected error while parsing the configuration data!", e);
			}

			return status;
		}

		private LoadStatus ParseSettings(Stream data, AppSettings settings, PasswordParameters password)
		{
			var status = TryParseData(data, out _, out _, out var rawData, password);

			if (status == LoadStatus.Success)
			{
				status = dataValidator.Validate(rawData) ? LoadStatus.Success : LoadStatus.InvalidData;
			}

			if (status == LoadStatus.Success)
			{
				dataMapper.Map(rawData, settings);
				dataProcessor.Process(rawData, settings);
			}

			return status;
		}

		public LoadStatus TryLoadData(Uri resource, out Stream data)
		{
			var status = LoadStatus.NotSupported;

			data = default;

			try
			{
				if (resource.IsFile || string.Equals(resource.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
				{
					if (File.Exists(resource.LocalPath))
					{
						data = new MemoryStream(File.ReadAllBytes(resource.LocalPath));
						status = LoadStatus.Success;
					}
					else
					{
						logger.Error($"The file '{resource.LocalPath}' does not exist!");
						status = LoadStatus.InvalidData;
					}
				}
				else if (resource.Scheme == Uri.UriSchemeHttp || resource.Scheme == Uri.UriSchemeHttps)
				{
					using (var client = new HttpClient())
					{
						var response = client.GetAsync(resource).GetAwaiter().GetResult();

						if (response.IsSuccessStatusCode)
						{
							var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

							data = new MemoryStream(bytes);
							status = LoadStatus.Success;
						}
						else
						{
							logger.Error($"Failed to download configuration from '{resource}' -> {response.StatusCode}!");
							status = LoadStatus.InvalidData;
						}
					}
				}
				else if (string.Equals(resource.Scheme, "data", StringComparison.OrdinalIgnoreCase))
				{
					// data:[<mediatype>][;base64],<data> — the standard way SEB configurations
					// are distributed without a file or server.
					var commaIndex = resource.OriginalString.IndexOf(',');

					if (commaIndex > 0)
					{
						var meta = resource.OriginalString.Substring(0, commaIndex);
						var payload = resource.OriginalString.Substring(commaIndex + 1);

						try
						{
							data = new MemoryStream(meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
								? Convert.FromBase64String(payload)
								: Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload)));

							status = LoadStatus.Success;
						}
						catch (FormatException)
						{
							logger.Error($"The data URI configuration '{resource.OriginalString[..Math.Min(64, resource.OriginalString.Length)]}...' does not contain valid base64 data!");

							status = LoadStatus.InvalidData;
						}
					}
					else
					{
						logger.Error("The data URI configuration does not contain a data part!");

						status = LoadStatus.InvalidData;
					}
				}

				logger.Info($"Tried to load data from '{resource}' -> Result: {status}.");
			}
			catch (Exception e)
			{
				logger.Error($"Failed to load data from '{resource}'!", e);
				status = LoadStatus.UnexpectedError;
			}

			return status;
		}

		public LoadStatus TryParseData(Stream data, out EncryptionParameters encryption, out FormatType format, out IDictionary<string, object> rawData, PasswordParameters password = null)
		{
			var parser = default(object);
			var status = LoadStatus.NotSupported;

			encryption = default;
			format = default;
			rawData = default(Dictionary<string, object>);

			if (binaryParser.CanParse(data))
			{
				parser = binaryParser;
			}
			else if (xmlParser.CanParse(data))
			{
				parser = xmlParser;
			}

			if (parser == binaryParser)
			{
				var result = binaryParser.TryParse(data, password);

				encryption = result.Encryption;
				format = result.Format;
				rawData = result.RawData;
				status = result.Status;
			}
			else if (parser == xmlParser)
			{
				var result = xmlParser.TryParse(data, password);

				encryption = result.Encryption;
				format = result.Format;
				rawData = result.RawData;
				status = result.Status;
			}

			logger.Info($"Tried to parse configuration data -> {(parser == null ? "no suitable parser found" : $"Result: {status}")}.");

			return status;
		}

		public SaveStatus SaveConfiguration(IDictionary<string, object> rawData, string destination, PasswordParameters password = null, bool compressed = true, bool usePlainDataBlock = false)
		{
			var result = binarySerializer.TrySerialize(rawData, password, compressed, usePlainDataBlock);

			if (result.Status == SaveStatus.Success)
			{
				try
				{
					using (var file = File.Create(destination))
					{
						result.Data.Seek(0, SeekOrigin.Begin);
						result.Data.CopyTo(file);
					}

					logger.Info($"Successfully saved configuration to '{destination}'.");
				}
				catch (Exception e)
				{
					logger.Error($"Failed to save configuration to '{destination}'!", e);

					result.Status = SaveStatus.UnexpectedError;
				}
			}

			return result.Status;
		}
	}
}
