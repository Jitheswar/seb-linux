/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Client on Windows).
 */

using System;
using System.IO;
using SafeExamBrowser.Linux.Configuration;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Client
{
	/// <summary>
	/// The result of loading a SEB configuration.
	/// </summary>
	public sealed class ConfigurationLoadResult
	{
		/// <summary>
		/// The settings loaded from the configuration, or the default settings if loading failed.
		/// </summary>
		public AppSettings Settings { get; set; }

		/// <summary>
		/// The resource from which the configuration was to be loaded.
		/// </summary>
		public Uri Source { get; set; }

		/// <summary>
		/// The status of the load operation.
		/// </summary>
		public LoadStatus Status { get; set; }
	}

	/// <summary>
	/// Loads SEB configurations from files, URLs or data URIs and calculates the configuration
	/// keys, see also SafeExamBrowser.Client (Windows).
	/// </summary>
	public sealed class ConfigurationLoader
	{
		private readonly ILogger logger;

		public ConfigurationLoader(ILogger logger)
		{
			this.logger = logger;
		}

		/// <summary>
		/// Loads the configuration from the given file path or URL.
		/// </summary>
		public ConfigurationLoadResult Load(string pathOrUrl, string password = null)
		{
			var source = ToResource(pathOrUrl);
			var repository = new ConfigurationRepository(logger);
			var status = repository.TryLoadSettings(source, out var settings, PasswordParameters(password));

			if (status != LoadStatus.Success)
			{
				logger.Error($"Failed to load the configuration from '{source}' -> {status}.");
			}

			return new ConfigurationLoadResult { Settings = settings, Source = source, Status = status };
		}

		/// <summary>
		/// Calculates the config key of the given file, URL or data URI without fully loading it
		/// into settings (see https://safeexambrowser.org/developer/seb-config-key.html).
		/// </summary>
		public string CalculateConfigurationKey(string pathOrUrl, string password = null)
		{
			var source = ToResource(pathOrUrl);
			var repository = new ConfigurationRepository(logger);
			var status = repository.TryLoadData(source, out var stream);

			if (status != LoadStatus.Success)
			{
				return default;
			}

			using (stream)
			{
				status = repository.TryParseData(stream, out _, out _, out var rawData, PasswordParameters(password));

				return status == LoadStatus.Success ? ConfigurationRepository.CalculateConfigurationKey(rawData) : default;
			}
		}

		private static PasswordParameters PasswordParameters(string password)
		{
			return password == default ? default : new PasswordParameters { Password = password };
		}

		private static Uri ToResource(string pathOrUrl)
		{
			// Absolute URIs (file://, http://, https://, data:) are used as-is, all other
			// values are treated as file system paths.
			if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri))
			{
				return uri;
			}

			return new Uri(Path.GetFullPath(pathOrUrl));
		}
	}
}
