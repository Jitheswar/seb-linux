/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Server;

namespace SafeExamBrowser.Linux.Tests
{
	public class SebServerClientTests
	{
		private const string DISCOVERY = """
		{
			"title": "Safe Exam Browser Server / Exam API Description",
			"server-location": "https://seb-server.example.org",
			"api-versions": [
				{
					"name": "v1",
					"endpoints": [
						{ "name": "access-token-endpoint", "location": "/oauth/token", "authorization": "Basic" },
						{ "name": "seb-handshake-endpoint", "location": "/exam-api/v1/handshake", "authorization": "Bearer" },
						{ "name": "seb-configuration-endpoint", "location": "/exam-api/v1/examconfig", "authorization": "Bearer" },
						{ "name": "seb-ping-endpoint", "location": "/exam-api/v1/sebping", "authorization": "Bearer" },
						{ "name": "seb-log-endpoint", "location": "/exam-api/v1/seblog", "authorization": "Bearer" }
					]
				}
			]
		}
		""";

		private const string TOKEN = """{ "access_token": "token-1", "token_type": "bearer" }""";
		private const string EXAMS = """
		[
			{ "examId": 1, "name": "Math", "url": "https://moodle.example.org/mod/quiz/view.php?id=1", "lmsType": "MOODLE" },
			{ "examId": 2, "name": "Physics", "url": "https://moodle.example.org/mod/quiz/view.php?id=2", "lmsType": "MOODLE" }
		]
		""";

		[Fact]
		public async Task MustLoadApiAndRequestAccessToken()
		{
			var (client, handler) = CreateClient();

			handler.EnqueueJson(DISCOVERY);
			handler.EnqueueJson(TOKEN);

			await client.AuthenticateAsync();

			Assert.Equal("oauth/token", client.Api.AccessTokenEndpoint);
			Assert.Equal("exam-api/v1/handshake", client.Api.HandshakeEndpoint);
			Assert.Equal("exam-api/v1/examconfig", client.Api.ConfigurationEndpoint);
			Assert.Equal("exam-api/v1/sebping", client.Api.PingEndpoint);
			Assert.Equal("exam-api/v1/seblog", client.Api.LogEndpoint);

			Assert.Equal(2, handler.Requests.Count);

			var discovery = handler.Requests[0];

			Assert.Equal("GET", discovery.Method);
			Assert.Equal("https://seb-server.example.org/exam-api/discovery", discovery.Uri);

			var token = handler.Requests[1];

			Assert.Equal("POST", token.Method);
			Assert.Equal("https://seb-server.example.org/oauth/token", token.Uri);
			Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client-name:client-secret")), token.Header("Authorization"));
			Assert.Contains("grant_type=client_credentials", token.Body);
			Assert.Contains("scope=read+write", token.Body);
			Assert.Equal("application/x-www-form-urlencoded", token.ContentType);
		}

		[Fact]
		public async Task MustRejectUnsupportedApiVersion()
		{
			var (client, handler) = CreateClient();

			handler.EnqueueJson("""{ "api-versions": [ { "name": "v9", "endpoints": [] } ] }""");

			var exception = await Assert.ThrowsAsync<SebServerException>(() => client.AuthenticateAsync());

			Assert.Contains("v1", exception.Message);
		}

		[Fact]
		public async Task MustRejectIncompleteApi()
		{
			var (client, handler) = CreateClient();

			handler.EnqueueJson("""{ "api-versions": [ { "name": "v1", "endpoints": [ { "name": "access-token-endpoint", "location": "/oauth/token" } ] } ] }""");

			var exception = await Assert.ThrowsAsync<SebServerException>(() => client.AuthenticateAsync());

			Assert.Contains("endpoints", exception.Message);
		}

		[Fact]
		public async Task MustPerformHandshakeAndParseRunningExams()
		{
			var (client, handler) = CreateClient();

			await AuthenticateAsync(client, handler);

			handler.EnqueueJson(EXAMS, configure: response => response.Headers.Add("SEBConnectionToken", "connection-token-1"));

			var exams = await client.HandshakeAsync();

			Assert.Equal("connection-token-1", client.ConnectionToken);
			Assert.Equal(2, exams.Count);
			Assert.Equal("1", exams[0].Id);
			Assert.Equal("Math", exams[0].Name);
			Assert.Equal("https://moodle.example.org/mod/quiz/view.php?id=1", exams[0].Url);
			Assert.Equal("MOODLE", exams[0].LmsType);
			Assert.Equal("2", exams[1].Id);

			var request = handler.Last();

			Assert.Equal("POST", request.Method);
			Assert.Equal("https://seb-server.example.org/exam-api/v1/handshake", request.Uri);
			Assert.Equal("Bearer token-1", request.Header("Authorization"));
			Assert.Contains("institutionId=institution-1", request.Body);
			Assert.Contains("client_id=user", request.Body);
			Assert.Contains("seb_machine_name=machine", request.Body);
			Assert.Contains("seb_os_name=Linux", request.Body);
			Assert.Contains("seb_version=0.1.0", request.Body);
			Assert.DoesNotContain("examId", request.Body);
		}

		[Fact]
		public async Task MustProvideExamIdWhenPerformingHandshake()
		{
			var (client, handler) = CreateClient();

			await AuthenticateAsync(client, handler);

			handler.EnqueueJson(EXAMS, configure: response => response.Headers.Add("SEBConnectionToken", "connection-token-1"));

			await client.HandshakeAsync("2");

			Assert.Contains("examId=2", handler.Last().Body);
		}

		[Fact]
		public async Task MustRejectHandshakeWithoutConnectionToken()
		{
			var (client, handler) = CreateClient();

			await AuthenticateAsync(client, handler);

			handler.EnqueueJson(EXAMS);

			var exception = await Assert.ThrowsAsync<SebServerException>(() => client.HandshakeAsync());

			Assert.Contains("connection token", exception.Message);
		}

		[Fact]
		public async Task MustSelectExamAndProvideSignatureData()
		{
			var (client, handler) = CreateClient();

			await HandshakeAsync(client, handler);

			handler.EnqueueJson("[]", configure: response =>
			{
				response.Headers.Add("SEBExamSalt", "00112233445566778899aabbccddeeff");
				response.Headers.Add("SEBServerBEK", "browser-exam-key-1");
			});

			var (salt, browserExamKey) = await client.SelectExamAsync("7");

			Assert.Equal("00112233445566778899aabbccddeeff", salt);
			Assert.Equal("browser-exam-key-1", browserExamKey);
			Assert.Equal("browser-exam-key-1", client.BrowserExamKey);

			var request = handler.Last();

			Assert.Equal("PATCH", request.Method);
			Assert.Equal("https://seb-server.example.org/exam-api/v1/handshake", request.Uri);
			Assert.Equal("Bearer token-1", request.Header("Authorization"));
			Assert.Equal("connection-token-1", request.Header("SEBConnectionToken"));
			Assert.Equal("examId=7", request.Body);
		}

		[Fact]
		public async Task MustEstablishConnectionWithAppSignatureKey()
		{
			var (client, handler) = CreateClient();

			await HandshakeAsync(client, handler);

			handler.EnqueueJson("[]");

			await client.EstablishConnectionAsync("signature-key-1");

			var request = handler.Last();

			Assert.Equal("PUT", request.Method);
			Assert.Equal("seb_signature_key=signature-key-1", request.Body);

			handler.EnqueueJson("[]");

			await client.EstablishConnectionAsync();

			Assert.Equal(string.Empty, handler.Last().Body);
		}

		[Fact]
		public async Task MustDownloadExamConfiguration()
		{
			var (client, handler) = CreateClient();

			await HandshakeAsync(client, handler);

			handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) });

			var data = await client.GetExamConfigurationAsync("7");

			Assert.Equal(new byte[] { 1, 2, 3, 4 }, data);

			var request = handler.Last();

			Assert.Equal("GET", request.Method);
			Assert.Equal("https://seb-server.example.org/exam-api/v1/examconfig?examId=7", request.Uri);
			Assert.Equal("connection-token-1", request.Header("SEBConnectionToken"));
		}

		[Fact]
		public async Task MustSendPings()
		{
			var (client, handler) = CreateClient();

			await HandshakeAsync(client, handler);

			handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

			var instruction = await client.PingAsync(1);

			Assert.Null(instruction);

			var first = handler.Last();

			Assert.Equal("POST", first.Method);
			Assert.Equal("https://seb-server.example.org/exam-api/v1/sebping", first.Uri);
			Assert.Contains("ping-number=1", first.Body);
			Assert.Contains("timestamp=", first.Body);
			Assert.DoesNotContain("instruction-confirm", first.Body);

			handler.EnqueueJson("""{ "instruction": "SEB_QUIT", "attributes": { "instruction-confirm": "confirm-1" } }""");

			instruction = await client.PingAsync(2, "confirm-0");

			Assert.Equal("SEB_QUIT", instruction.Instruction);
			Assert.Equal("confirm-1", instruction.InstructionConfirmation);
			Assert.Contains("ping-number=2", handler.Last().Body);
			Assert.Contains("instruction-confirm=confirm-0", handler.Last().Body);
		}

		[Fact]
		public async Task MustCloseConnection()
		{
			var (client, handler) = CreateClient();

			await HandshakeAsync(client, handler);

			handler.EnqueueJson(string.Empty);

			await client.CloseAsync();

			Assert.Equal("DELETE", handler.Last().Method);
			Assert.Equal("delete=true", handler.Last().Body);
			Assert.Null(client.ConnectionToken);
		}

		[Fact]
		public async Task MustRefreshExpiredAccessToken()
		{
			var (client, handler) = CreateClient();

			await AuthenticateAsync(client, handler);

			handler.EnqueueJson("""{ "error": "invalid_token" }""", HttpStatusCode.Unauthorized);
			handler.EnqueueJson("""{ "access_token": "token-2" }""");
			handler.EnqueueJson(EXAMS, configure: response => response.Headers.Add("SEBConnectionToken", "connection-token-1"));

			var exams = await client.HandshakeAsync();

			Assert.Equal(2, exams.Count);
			Assert.Equal("Bearer token-1", handler.Requests[2].Header("Authorization"));
			Assert.Equal("POST", handler.Requests[3].Method);
			Assert.Equal("https://seb-server.example.org/oauth/token", handler.Requests[3].Uri);
			Assert.Equal("Bearer token-2", handler.Requests[4].Header("Authorization"));
			Assert.Equal("connection-token-1", client.ConnectionToken);
		}

		[Fact]
		public async Task MustThrowOnServerError()
		{
			var (client, handler) = CreateClient();

			await AuthenticateAsync(client, handler);

			handler.EnqueueJson("""{ "error": "internal_server_error" }""", HttpStatusCode.InternalServerError);

			var exception = await Assert.ThrowsAsync<SebServerException>(() => client.HandshakeAsync());

			Assert.Contains("handshake", exception.Message);
		}

		[Fact]
		public void MustValidateConnection()
		{
			Assert.Contains("SEB Server URL", new SebServerConnection().Validate());
			Assert.Contains("URL", new SebServerConnection { ServerUrl = "not a url" }.Validate());
			Assert.Contains("URL", new SebServerConnection { ServerUrl = "ftp://seb-server.example.org" }.Validate());
			Assert.Null(new SebServerConnection { ServerUrl = "https://seb-server.example.org" }.Validate());
			Assert.Equal("/exam-api/discovery", new SebServerConnection().GetApiEndpoint());
			Assert.Equal("/custom/discovery", new SebServerConnection { ApiEndpoint = "/custom/discovery" }.GetApiEndpoint());
			Assert.Null(SebServerConnection.FromSettings(default));
			Assert.Null(SebServerConnection.FromSettings(new SafeExamBrowser.Linux.Configuration.Settings.ServerSettings()));
		}

		private static (SebServerClient Client, StubHttpMessageHandler Handler) CreateClient()
		{
			var handler = new StubHttpMessageHandler();
			var http = new HttpClient(handler) { BaseAddress = new Uri("https://seb-server.example.org/") };
			var connection = new SebServerConnection
			{
				ServerUrl = "https://seb-server.example.org",
				ClientName = "client-name",
				ClientSecret = "client-secret",
				Institution = "institution-1",
				UserName = "user",
				MachineName = "machine",
				OperatingSystem = "Linux",
				ProgramVersion = "0.1.0"
			};

			return (new SebServerClient(connection, new ConsoleLogger("test"), http), handler);
		}

		private static async Task AuthenticateAsync(SebServerClient client, StubHttpMessageHandler handler)
		{
			handler.EnqueueJson(DISCOVERY);
			handler.EnqueueJson(TOKEN);

			await client.AuthenticateAsync();
		}

		private static async Task HandshakeAsync(SebServerClient client, StubHttpMessageHandler handler)
		{
			await AuthenticateAsync(client, handler);

			handler.EnqueueJson(EXAMS, configure: response => response.Headers.Add("SEBConnectionToken", "connection-token-1"));

			await client.HandshakeAsync();
		}
	}
}
