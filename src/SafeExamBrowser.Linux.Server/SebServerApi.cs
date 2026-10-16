/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Server.Data.Api on Windows).
 */

using System;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// The endpoints of the SEB Server Exam API, as advertised by the API discovery document
	/// (see https://github.com/SafeExamBrowser/seb-server, API v1).
	/// </summary>
	public sealed class SebServerApi
	{
		public const string VERSION_V1 = "v1";

		public const string ACCESS_TOKEN_ENDPOINT = "access-token-endpoint";
		public const string HANDSHAKE_ENDPOINT = "seb-handshake-endpoint";
		public const string CONFIGURATION_ENDPOINT = "seb-configuration-endpoint";
		public const string PING_ENDPOINT = "seb-ping-endpoint";
		public const string LOG_ENDPOINT = "seb-log-endpoint";

		/// <summary>
		/// The path of the OAuth2 access token endpoint, relative to the server base address.
		/// </summary>
		public string AccessTokenEndpoint { get; set; }

		/// <summary>
		/// The path of the handshake endpoint, relative to the server base address.
		/// </summary>
		public string HandshakeEndpoint { get; set; }

		/// <summary>
		/// The path of the exam configuration endpoint, relative to the server base address.
		/// </summary>
		public string ConfigurationEndpoint { get; set; }

		/// <summary>
		/// The path of the ping endpoint, relative to the server base address.
		/// </summary>
		public string PingEndpoint { get; set; }

		/// <summary>
		/// The path of the log endpoint, relative to the server base address.
		/// </summary>
		public string LogEndpoint { get; set; }

		/// <summary>
		/// Validates that all endpoints required by this client are available.
		/// </summary>
		public string Validate()
		{
			if (string.IsNullOrWhiteSpace(AccessTokenEndpoint) ||
				string.IsNullOrWhiteSpace(HandshakeEndpoint) ||
				string.IsNullOrWhiteSpace(ConfigurationEndpoint) ||
				string.IsNullOrWhiteSpace(PingEndpoint))
			{
				return $"The SEB Server API {VERSION_V1} does not provide all required endpoints " +
					$"(access token: '{AccessTokenEndpoint}', handshake: '{HandshakeEndpoint}', " +
					$"configuration: '{ConfigurationEndpoint}', ping: '{PingEndpoint}')!";
			}

			return default;
		}

		/// <summary>
		/// Removes a leading slash, as the endpoints are used relative to the server base address.
		/// </summary>
		public void Sanitize()
		{
			AccessTokenEndpoint = No(this.AccessTokenEndpoint);
			HandshakeEndpoint = No(this.HandshakeEndpoint);
			ConfigurationEndpoint = No(this.ConfigurationEndpoint);
			PingEndpoint = No(this.PingEndpoint);
			LogEndpoint = No(this.LogEndpoint);
		}

		private static string No(string path)
		{
			return path == default ? default : path.TrimStart('/');
		}
	}
}
