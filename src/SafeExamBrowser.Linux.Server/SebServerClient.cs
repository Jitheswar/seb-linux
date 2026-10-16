/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Server.ServerProxy and
 * SafeExamBrowser.Server.Requests on Windows). Implements the client side of the SEB
 * Server Exam API v1, see https://github.com/SafeExamBrowser/seb-server.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Configuration.Contracts;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// A client for the SEB Server Exam API v1 (https://github.com/SafeExamBrowser/seb-server).
	/// Implements the full session flow: API discovery, OAuth2 token retrieval, handshake,
	/// exam selection, exam configuration download, pings (including instructions) and
	/// disconnection. See also SafeExamBrowser.Server.ServerProxy on Windows.
	/// </summary>
	public sealed class SebServerClient : IDisposable
	{
		internal const string HEADER_AUTHORIZATION = "Authorization";
		internal const string HEADER_CONNECTION_TOKEN = "SEBConnectionToken";
		internal const string HEADER_APP_SIGNATURE_KEY_SALT = "SEBExamSalt";
		internal const string HEADER_BROWSER_EXAM_KEY = "SEBServerBEK";

		private readonly SebServerConnection connection;
		private readonly HttpClient http;
		private readonly bool ownsHttpClient;
		private readonly ILogger logger;

		private string accessToken;
		private string connectionToken;

		public SebServerClient(SebServerConnection connection, ILogger logger = null, HttpClient httpClient = null)
		{
			this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
			this.logger = logger ?? new ConsoleLogger("SEB Server");

			if (httpClient == default)
			{
				var baseAddress = connection.ServerUrl.EndsWith("/") ? connection.ServerUrl : $"{connection.ServerUrl}/";

				http = new HttpClient { BaseAddress = new Uri(baseAddress) };
				ownsHttpClient = true;
			}
			else
			{
				http = httpClient;
			}

			if (connection.RequestTimeout > 0)
			{
				http.Timeout = TimeSpan.FromMilliseconds(connection.RequestTimeout);
			}
		}

		/// <summary>
		/// The endpoints of the SEB Server API, available after <see cref="AuthenticateAsync"/>.
		/// </summary>
		public SebServerApi Api { get; } = new SebServerApi();

		/// <summary>
		/// The connection token of the SEB Server session, available after the handshake.
		/// </summary>
		public string ConnectionToken => connectionToken;

		/// <summary>
		/// The app signature key salt provided by the SEB Server, if any. Used to calculate
		/// the app signature key, see <see cref="AppSignatureKey.Compute"/>.
		/// </summary>
		public string AppSignatureKeySalt { get; private set; }

		/// <summary>
		/// The custom browser exam key provided by the SEB Server, if any. Must be used for
		/// all requests during the exam instead of the key calculated from the configuration salt.
		/// </summary>
		public string BrowserExamKey { get; private set; }

		/// <summary>
		/// The exams currently running on the SEB Server, available after <see cref="HandshakeAsync"/>.
		/// </summary>
		public IReadOnlyList<RunningExam> RunningExams { get; private set; } = Array.Empty<RunningExam>();

		/// <summary>
		/// Loads the SEB Server API discovery document and requests an OAuth2 access token
		/// using the client credentials grant.
		/// </summary>
		public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
		{
			await LoadApiAsync(cancellationToken);
			await RequestAccessTokenAsync(cancellationToken);
		}

		/// <summary>
		/// Performs the handshake with the SEB Server and returns the running exams. The
		/// connection token is required for all further requests. If an exam identifier is
		/// given, only the corresponding exam is selected.
		/// </summary>
		public async Task<IReadOnlyList<RunningExam>> HandshakeAsync(string examId = null, CancellationToken cancellationToken = default)
		{
			var form = new List<KeyValuePair<string, string>>
			{
				Pair("institutionId", connection.Institution ?? string.Empty),
				Pair("client_id", ResolveString(connection.UserName, () => Environment.UserName)),
				Pair("seb_machine_name", ResolveString(connection.MachineName, () => Environment.MachineName)),
				Pair("seb_os_name", ResolveString(connection.OperatingSystem, () => RuntimeInformation.OSDescription)),
				Pair("seb_version", ResolveString(connection.ProgramVersion, () => AppInfo.ProgramBuildVersion))
			};

			if (!string.IsNullOrWhiteSpace(examId))
			{
				form.Add(Pair("examId", examId));
			}

			var response = await SendAsync(HttpMethod.Post, Api.HandshakeEndpoint, form, authorized: true, cancellationToken);
			using (response)
			{
				EnsureSuccess(response, HttpMethod.Post, "handshake");

				connectionToken = GetHeader(response, HEADER_CONNECTION_TOKEN);

				if (string.IsNullOrWhiteSpace(connectionToken))
				{
					throw new SebServerException("The SEB Server did not provide a connection token!");
				}

				UpdateSignatureData(response);
				logger.Info("Successfully performed the handshake with the SEB Server.");

				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				var exams = ParseExams(content);

				RunningExams = exams;

				return exams;
			}
		}

		/// <summary>
		/// Selects the given exam (PATCH handshake) and returns the app signature key salt and
		/// the custom browser exam key provided by the SEB Server.
		/// </summary>
		public async Task<(string AppSignatureKeySalt, string BrowserExamKey)> SelectExamAsync(string examId, CancellationToken cancellationToken = default)
		{
			var form = new List<KeyValuePair<string, string>> { Pair("examId", examId) };
			var response = await SendAsync(new HttpMethod("PATCH"), Api.HandshakeEndpoint, form, authorized: true, cancellationToken);

			using (response)
			{
				EnsureSuccess(response, new HttpMethod("PATCH"), "select exam");
				UpdateSignatureData(response);
				logger.Info($"Successfully selected exam '{examId}'.");

				return (AppSignatureKeySalt, BrowserExamKey);
			}
		}

		/// <summary>
		/// Finishes the handshake (PUT handshake) with the given app signature key, see
		/// <see cref="AppSignatureKey.Compute"/>.
		/// </summary>
		public async Task EstablishConnectionAsync(string appSignatureKey = null, CancellationToken cancellationToken = default)
		{
			var form = new List<KeyValuePair<string, string>>();

			if (!string.IsNullOrWhiteSpace(appSignatureKey))
			{
				form.Add(Pair("seb_signature_key", appSignatureKey));
			}

			var response = await SendAsync(HttpMethod.Put, Api.HandshakeEndpoint, form, authorized: true, cancellationToken);

			using (response)
			{
				EnsureSuccess(response, HttpMethod.Put, "establish connection");
				UpdateSignatureData(response);
				logger.Info("Successfully established the connection with the SEB Server.");
			}
		}

		/// <summary>
		/// Retrieves the configuration of the given exam from the SEB Server.
		/// </summary>
		public async Task<byte[]> GetExamConfigurationAsync(string examId, CancellationToken cancellationToken = default)
		{
			var path = $"{Api.ConfigurationEndpoint}?examId={Uri.EscapeDataString(examId)}";
			var response = await SendAsync(HttpMethod.Get, path, default, authorized: true, cancellationToken);

			using (response)
			{
				EnsureSuccess(response, HttpMethod.Get, "download exam configuration");

				var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

				logger.Info($"Successfully retrieved the exam configuration ({data.Length} bytes).");

				return data;
			}
		}

		/// <summary>
		/// Sends a ping to the SEB Server and returns the instruction to be processed, or null
		/// if there is none. The confirmation of a previously received instruction can be sent
		/// along with the ping.
		/// </summary>
		public async Task<SebServerInstruction> PingAsync(int pingNumber, string instructionConfirmation = null, CancellationToken cancellationToken = default)
		{
			var form = new List<KeyValuePair<string, string>>
			{
				Pair("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
				Pair("ping-number", pingNumber.ToString(CultureInfo.InvariantCulture))
			};

			if (!string.IsNullOrWhiteSpace(instructionConfirmation))
			{
				form.Add(Pair("instruction-confirm", instructionConfirmation));
			}

			var response = await SendAsync(HttpMethod.Post, Api.PingEndpoint, form, authorized: true, cancellationToken);

			using (response)
			{
				if (response.StatusCode == HttpStatusCode.NoContent)
				{
					return default;
				}

				EnsureSuccess(response, HttpMethod.Post, "ping");

				var content = await response.Content.ReadAsStringAsync(cancellationToken);

				if (string.IsNullOrWhiteSpace(content))
				{
					return default;
				}

				if (!SebServerInstruction.TryParse(content, out var instruction))
				{
					logger.Warn($"Failed to parse the received instruction: '{content}'.");
					return default;
				}

				logger.Info($"Received instruction '{instruction.Instruction}'.");

				return instruction;
			}
		}

		/// <summary>
		/// Terminates the connection with the SEB Server (DELETE handshake).
		/// </summary>
		public async Task CloseAsync(CancellationToken cancellationToken = default)
		{
			var form = new List<KeyValuePair<string, string>> { Pair("delete", "true") };
			var response = await SendAsync(HttpMethod.Delete, Api.HandshakeEndpoint, form, authorized: true, cancellationToken);

			using (response)
			{
				EnsureSuccess(response, HttpMethod.Delete, "close connection");
				logger.Info("Successfully terminated the connection with the SEB Server.");
			}

			connectionToken = default;
		}

		public void Dispose()
		{
			if (ownsHttpClient)
			{
				http.Dispose();
			}
		}

		private async Task LoadApiAsync(CancellationToken cancellationToken)
		{
			var path = connection.GetApiEndpoint().TrimStart('/');
			var response = await SendAsync(HttpMethod.Get, path, default, authorized: false, cancellationToken);

			using (response)
			{
				EnsureSuccess(response, HttpMethod.Get, "load the server API");

				var content = await response.Content.ReadAsStringAsync(cancellationToken);

				ParseApi(content);
				logger.Info($"Successfully loaded the SEB Server API (handshake: '{Api.HandshakeEndpoint}').");
			}
		}

		private async Task RequestAccessTokenAsync(CancellationToken cancellationToken)
		{
			var secret = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.ClientName}:{connection.ClientSecret}"));
			var form = new List<KeyValuePair<string, string>>
			{
				Pair("grant_type", "client_credentials"),
				Pair("scope", "read write")
			};

			var request = BuildRequest(HttpMethod.Post, Api.AccessTokenEndpoint, form);
			request.Headers.TryAddWithoutValidation(HEADER_AUTHORIZATION, $"Basic {secret}");

			var response = await SendRequestAsync(request, cancellationToken);

			using (response)
			{
				EnsureSuccess(response, HttpMethod.Post, "request the access token");

				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				accessToken = ParseAccessToken(content);

				if (string.IsNullOrWhiteSpace(accessToken))
				{
					throw new SebServerException("The SEB Server did not provide an OAuth2 access token!");
				}

				logger.Info("Successfully retrieved an OAuth2 access token from the SEB Server.");
			}
		}

		private void ParseApi(string content)
		{
			try
			{
				using (var document = JsonDocument.Parse(content))
				{
					var root = document.RootElement;

					if (root.ValueKind != JsonValueKind.Object ||
						!root.TryGetProperty("api-versions", out var versions) ||
						versions.ValueKind != JsonValueKind.Array)
					{
						throw new SebServerException("The SEB Server API discovery document is invalid!");
					}

					JsonElement? v1 = default;

					foreach (var version in versions.EnumerateArray())
					{
						if (version.ValueKind == JsonValueKind.Object &&
							version.TryGetProperty("name", out var name) &&
							string.Equals(name.GetString(), SebServerApi.VERSION_V1, StringComparison.OrdinalIgnoreCase))
						{
							v1 = version;
							break;
						}
					}

					if (!v1.HasValue)
					{
						throw new SebServerException($"The SEB Server instance does not support the required API version {SebServerApi.VERSION_V1}!");
					}

					if (!v1.Value.TryGetProperty("endpoints", out var endpoints) || endpoints.ValueKind != JsonValueKind.Array)
					{
						throw new SebServerException($"The SEB Server API {SebServerApi.VERSION_V1} does not provide any endpoints!");
					}

					foreach (var endpoint in endpoints.EnumerateArray())
					{
						if (endpoint.ValueKind != JsonValueKind.Object)
						{
							continue;
						}

						var name = endpoint.TryGetProperty("name", out var n) ? n.GetString() : default;
						var location = endpoint.TryGetProperty("location", out var l) ? l.GetString() : default;

						switch (name)
						{
							case SebServerApi.ACCESS_TOKEN_ENDPOINT:
								Api.AccessTokenEndpoint = location;
								break;
							case SebServerApi.HANDSHAKE_ENDPOINT:
								Api.HandshakeEndpoint = location;
								break;
							case SebServerApi.CONFIGURATION_ENDPOINT:
								Api.ConfigurationEndpoint = location;
								break;
							case SebServerApi.PING_ENDPOINT:
								Api.PingEndpoint = location;
								break;
							case SebServerApi.LOG_ENDPOINT:
								Api.LogEndpoint = location;
								break;
						}
					}
				}
			}
			catch (JsonException e)
			{
				throw new SebServerException("Failed to parse the SEB Server API discovery document!", e);
			}

			Api.Sanitize();

			var problem = Api.Validate();

			if (problem != default)
			{
				throw new SebServerException(problem);
			}
		}

		private static string ParseAccessToken(string content)
		{
			try
			{
				using (var document = JsonDocument.Parse(content))
				{
					return document.RootElement.ValueKind == JsonValueKind.Object &&
						document.RootElement.TryGetProperty("access_token", out var token)
						? token.GetString()
						: default;
				}
			}
			catch (JsonException)
			{
				return default;
			}
		}

		private static IReadOnlyList<RunningExam> ParseExams(string content)
		{
			var exams = new List<RunningExam>();

			if (string.IsNullOrWhiteSpace(content))
			{
				return exams;
			}

			try
			{
				using (var document = JsonDocument.Parse(content))
				{
					if (document.RootElement.ValueKind != JsonValueKind.Array)
					{
						return exams;
					}

					foreach (var element in document.RootElement.EnumerateArray())
					{
						if (element.ValueKind != JsonValueKind.Object)
						{
							continue;
						}

						exams.Add(new RunningExam
						{
							Id = GetString(element, "examId"),
							LmsType = GetString(element, "lmsType"),
							Name = GetString(element, "name"),
							Url = GetString(element, "url")
						});
					}
				}
			}
			catch (JsonException e)
			{
				throw new SebServerException("Failed to parse the running exams provided by the SEB Server!", e);
			}

			return exams;
		}

		private static string GetString(JsonElement element, string name)
		{
			if (!element.TryGetProperty(name, out var value))
			{
				return default;
			}

			return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
		}

		private async Task<HttpResponseMessage> SendAsync(
			HttpMethod method,
			string path,
			IEnumerable<KeyValuePair<string, string>> form,
			bool authorized,
			CancellationToken cancellationToken,
			bool allowTokenRefresh = true)
		{
			var request = BuildRequest(method, path, form);

			if (authorized && accessToken != default)
			{
				request.Headers.TryAddWithoutValidation(HEADER_AUTHORIZATION, $"Bearer {accessToken}");
			}

			if (authorized && connectionToken != default)
			{
				request.Headers.TryAddWithoutValidation(HEADER_CONNECTION_TOKEN, connectionToken);
			}

			var response = await SendRequestAsync(request, cancellationToken);

			if (response.StatusCode == HttpStatusCode.Unauthorized && authorized && allowTokenRefresh)
			{
				var content = await response.Content.ReadAsStringAsync(cancellationToken);

				response.Dispose();

				if (IsTokenExpired(content))
				{
					logger.Info("The OAuth2 access token has expired, requesting a new one...");

					await RequestAccessTokenAsync(cancellationToken);

					return await SendAsync(method, path, form, authorized, cancellationToken, allowTokenRefresh: false);
				}

				throw new SebServerException($"The SEB Server rejected the request {method} '{path}' (Unauthorized): {Truncate(content)}");
			}

			return response;
		}

		private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			try
			{
				return await http.SendAsync(request, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (OperationCanceledException e)
			{
				throw new SebServerException($"The request {request.Method} '{request.RequestUri}' timed out!", e);
			}
			catch (HttpRequestException e)
			{
				throw new SebServerException($"The request {request.Method} '{request.RequestUri}' failed: {e.Message}", e);
			}
		}

		private static HttpRequestMessage BuildRequest(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>> form)
		{
			var request = new HttpRequestMessage(method, path);

			request.Headers.TryAddWithoutValidation("Accept", "application/json, */*");

			if (form != default)
			{
				request.Content = new FormUrlEncodedContent(form);
			}

			return request;
		}

		private static bool IsTokenExpired(string content)
		{
			try
			{
				using (var document = JsonDocument.Parse(content))
				{
					return document.RootElement.ValueKind == JsonValueKind.Object &&
						document.RootElement.TryGetProperty("error", out var error) &&
						string.Equals(error.GetString(), "invalid_token", StringComparison.OrdinalIgnoreCase);
				}
			}
			catch (JsonException)
			{
				return false;
			}
		}

		private static void EnsureSuccess(HttpResponseMessage response, HttpMethod method, string operation)
		{
			if (!response.IsSuccessStatusCode)
			{
				throw new SebServerException($"Failed to {operation}: the SEB Server returned {response.StatusCode} for {method} '{response.RequestMessage?.RequestUri}'!");
			}
		}

		private void UpdateSignatureData(HttpResponseMessage response)
		{
			var salt = GetHeader(response, HEADER_APP_SIGNATURE_KEY_SALT);

			if (salt != default)
			{
				AppSignatureKeySalt = salt;
				logger.Info("The SEB Server provided an app signature key salt.");
			}

			var browserExamKey = GetHeader(response, HEADER_BROWSER_EXAM_KEY);

			if (browserExamKey != default)
			{
				BrowserExamKey = browserExamKey;
				logger.Info("The SEB Server provided a custom browser exam key.");
			}
		}

		private static string GetHeader(HttpResponseMessage response, string name)
		{
			return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : default;
		}

		private static string ResolveString(string value, Func<string> fallback)
		{
			return string.IsNullOrWhiteSpace(value) ? fallback() : value;
		}

		private static KeyValuePair<string, string> Pair(string name, string value)
		{
			return new KeyValuePair<string, string>(name, value);
		}

		private static string Truncate(string content)
		{
			return content == default || content.Length <= 200 ? content : $"{content.Substring(0, 200)}...";
		}
	}
}
