/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.Linux.Configuration.Settings;
using SafeExamBrowser.Linux.Server;

namespace SafeExamBrowser.Linux.Tests
{
	public class SebServerConnectionTests
	{
		[Fact]
		public void FromSettingsMustReturnNullWithoutSettings()
		{
			Assert.Null(SebServerConnection.FromSettings(null));
		}

		[Fact]
		public void FromSettingsMustReturnNullWithoutServerUrl()
		{
			var settings = new ServerSettings();

			settings.ServerUrl = "   ";

			Assert.Null(SebServerConnection.FromSettings(settings));
		}

		[Fact]
		public void FromSettingsMustCopyTheConnectionParameters()
		{
			var settings = new ServerSettings();

			settings.ServerUrl = "https://seb-server.example.org";
			settings.ApiEndpoint = "/custom-api";
			settings.ClientName = "client";
			settings.ClientSecret = "secret";
			settings.Institution = "org";
			settings.ExamId = "42";

			var connection = SebServerConnection.FromSettings(settings);

			Assert.NotNull(connection);
			Assert.Equal("https://seb-server.example.org", connection.ServerUrl);
			Assert.Equal("/custom-api", connection.ApiEndpoint);
			Assert.Equal("client", connection.ClientName);
			Assert.Equal("secret", connection.ClientSecret);
			Assert.Equal("org", connection.Institution);
			Assert.Equal("42", connection.ExamId);
		}

		[Fact]
		public void GetApiEndpointMustUseTheDefaultWhenNotConfigured()
		{
			var connection = new SebServerConnection();

			connection.ServerUrl = "https://seb-server.example.org";

			Assert.Equal("/exam-api/discovery", connection.GetApiEndpoint());
		}

		[Fact]
		public void ValidateMustRejectAMissingServerUrl()
		{
			var connection = new SebServerConnection();

			Assert.Contains("SEB Server URL", connection.Validate());
		}

		[Fact]
		public void ValidateMustRejectANonHttpUrl()
		{
			var connection = new SebServerConnection();

			connection.ServerUrl = "ftp://seb-server.example.org";

			Assert.Contains("not a valid HTTP(S) URL", connection.Validate());
		}

		[Fact]
		public void ValidateMustRejectARelativeUrl()
		{
			var connection = new SebServerConnection();

			connection.ServerUrl = "seb-server.example.org/exam-api";

			Assert.Contains("not a valid HTTP(S) URL", connection.Validate());
		}

		[Theory]
		[InlineData("https://seb-server.example.org")]
		[InlineData("http://127.0.0.1:8765")]
		public void ValidateMustAcceptAValidUrl(string serverUrl)
		{
			var connection = new SebServerConnection();

			connection.ServerUrl = serverUrl;

			Assert.Null(connection.Validate());
		}
	}
}
