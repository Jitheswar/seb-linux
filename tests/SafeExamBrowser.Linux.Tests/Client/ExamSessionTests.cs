/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.Linux.Client;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Tests
{
	public class ExamSessionTests
	{
		[Fact]
		public void MustRejectMissingSettings()
		{
			Assert.NotNull(ExamSession.Validate(null));
		}

		[Fact]
		public void MustRejectMissingStartUrl()
		{
			var settings = new AppSettings();

			settings.Browser.StartUrl = "  ";

			Assert.Contains("start URL", ExamSession.Validate(settings));
		}

		[Fact]
		public void MustAcceptSettingsWithStartUrl()
		{
			var settings = new AppSettings();

			settings.Browser.StartUrl = "https://exam.example.org/";

			Assert.Null(ExamSession.Validate(settings));
		}
	}
}
