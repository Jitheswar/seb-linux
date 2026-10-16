/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Settings.Server on Windows).
 */

using System;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// The connection parameters for a SEB Server instance, see also
	/// https://github.com/SafeExamBrowser/seb-server. The values are either loaded from a
	/// configuration (see <see cref="FromSettings"/>) or specified programmatically.
	/// </summary>
	public sealed class SebServerConnection
	{
		public const string DEFAULT_API_ENDPOINT = "/exam-api/discovery";

		/// <summary>
		/// The base URL of the SEB Server, e.g. https://seb-server.example.org.
		/// </summary>
		public string ServerUrl { get; set; }

		/// <summary>
		/// The path of the SEB Server API discovery document, by default "/exam-api/discovery".
		/// </summary>
		public string ApiEndpoint { get; set; }

		/// <summary>
		/// The client name for the OAuth2 client credentials grant.
		/// </summary>
		public string ClientName { get; set; }

		/// <summary>
		/// The client secret for the OAuth2 client credentials grant.
		/// </summary>
		public string ClientSecret { get; set; }

		/// <summary>
		/// The institution identifier, if the SEB Server installation is used by multiple institutions.
		/// </summary>
		public string Institution { get; set; }

		/// <summary>
		/// The identifier of the exam to be run, or null to select one of the running exams.
		/// </summary>
		public string ExamId { get; set; }

		/// <summary>
		/// The request timeout in milliseconds, or 0 to use the default.
		/// </summary>
		public int RequestTimeout { get; set; }

		/// <summary>
		/// The client identifier (client_id) sent to the SEB Server, by default the name of the current user.
		/// </summary>
		public string UserName { get; set; }

		/// <summary>
		/// The machine name sent to the SEB Server, by default the host name of the computer.
		/// </summary>
		public string MachineName { get; set; }

		/// <summary>
		/// The operating system description sent to the SEB Server, by default the current OS description.
		/// </summary>
		public string OperatingSystem { get; set; }

		/// <summary>
		/// The SEB version sent to the SEB Server, by default the version of this build.
		/// </summary>
		public string ProgramVersion { get; set; }

		/// <summary>
		/// Creates connection parameters from the given server settings, or returns null if the
		/// settings do not define a SEB Server URL.
		/// </summary>
		public static SebServerConnection FromSettings(ServerSettings settings)
		{
			if (settings == default || string.IsNullOrWhiteSpace(settings.ServerUrl))
			{
				return default;
			}

			return new SebServerConnection
			{
				ServerUrl = settings.ServerUrl,
				ApiEndpoint = settings.ApiEndpoint,
				ClientName = settings.ClientName,
				ClientSecret = settings.ClientSecret,
				Institution = settings.Institution,
				ExamId = settings.ExamId
			};
		}

		/// <summary>
		/// Returns the API discovery path to be used, which is either the configured value or
		/// the default "/exam-api/discovery" (see https://github.com/SafeExamBrowser/seb-server).
		/// </summary>
		public string GetApiEndpoint()
		{
			return string.IsNullOrWhiteSpace(ApiEndpoint) ? DEFAULT_API_ENDPOINT : ApiEndpoint;
		}

		/// <summary>
		/// Validates the connection parameters. Returns null if they are valid, otherwise a
		/// description of the problem.
		/// </summary>
		public string Validate()
		{
			if (string.IsNullOrWhiteSpace(ServerUrl))
			{
				return "The configuration does not define a SEB Server URL (sebServerURL)!";
			}

			if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			{
				return $"The configured SEB Server URL '{ServerUrl}' is not a valid HTTP(S) URL!";
			}

			return default;
		}
	}
}
