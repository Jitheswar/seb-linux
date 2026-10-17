/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.Linux.Browser.Content;

namespace SafeExamBrowser.Linux.Tests.Browser
{
	public class ContentLoaderTests
	{
		private readonly ContentLoader sut = new ContentLoader();

		[Fact]
		public void LoadApi_MustReplaceAllPlaceholders()
		{
			var js = sut.LoadApi("bek-hash", "ck-hash", "SEB_Linux_0.1.0");

			Assert.Contains("version: 'SEB_Linux_0.1.0'", js);
			Assert.Contains("browserExamKey: 'bek-hash'", js);
			Assert.Contains("configKey: 'ck-hash'", js);
			Assert.DoesNotContain("%%_", js);
		}

	}
}
