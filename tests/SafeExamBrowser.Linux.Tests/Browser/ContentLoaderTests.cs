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
			var js = sut.LoadApi("bek-hash", "ck-hash", "0.1.0");

			Assert.Contains("version: 'SEB_Linux_0.1.0'", js);
			Assert.Contains("browserExamKey: 'bek-hash'", js);
			Assert.Contains("configKey: 'ck-hash'", js);
			Assert.DoesNotContain("%%_", js);
		}

		[Fact]
		public void LoadBlockedContent_MustReplaceMessage()
		{
			var html = sut.LoadBlockedContent("blocked!");

			Assert.Contains("blocked!", html);
			Assert.DoesNotContain("%%MESSAGE%%", html);
		}

		[Fact]
		public void LoadBlockedPage_MustReplaceAllPlaceholders()
		{
			var html = sut.LoadBlockedPage("title", "message", "back");

			Assert.Contains("<title>title</title>", html);
			Assert.Contains("message", html);
			Assert.Contains("back", html);
			Assert.DoesNotContain("%%", html);
		}

		[Fact]
		public void LoadIsolatedClipboard_MustProvideClipboardApi()
		{
			var js = sut.LoadIsolatedClipboard();

			Assert.Contains("SafeExamBrowser.clipboard", js);
			Assert.Contains("copy", js);
			Assert.Contains("paste", js);
			Assert.Contains("cut", js);
		}

		[Fact]
		public void LoadBlockClipboard_MustPreventDefault()
		{
			var js = sut.LoadBlockClipboard();

			Assert.Contains("preventDefault", js);
			Assert.Contains("'copy'", js);
			Assert.Contains("'paste'", js);
		}

		[Fact]
		public void LoadPageZoom_MustDisableZoom()
		{
			var js = sut.LoadPageZoom();

			Assert.Contains("wheel", js);
			Assert.Contains("preventDefault", js);
		}
	}
}
