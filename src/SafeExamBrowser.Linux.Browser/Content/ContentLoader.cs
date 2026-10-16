/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System.IO;
using System.Reflection;

namespace SafeExamBrowser.Linux.Browser.Content
{
	/// <summary>
	/// Loads the browser content (JavaScript and HTML resources) embedded into the application.
	/// </summary>
	internal class ContentLoader
	{
		private string api;
		private string blockClipboard;
		private string isolatedClipboard;
		private string pageZoom;

		internal string LoadApi(string browserExamKey, string configurationKey, string version)
		{
			if (api == default)
			{
				api = ReadResource("SebApi.js");
			}

			var js = api;

			js = js.Replace("%%_BEK_%%", browserExamKey);
			js = js.Replace("%%_CK_%%", configurationKey);
			js = js.Replace("%%_VERSION_%%", version);

			return js;
		}

		internal string LoadBlockClipboard()
		{
			if (blockClipboard == default)
			{
				blockClipboard = ReadResource("BlockClipboard.js");
			}

			return blockClipboard;
		}

		internal string LoadBlockedContent(string message)
		{
			return ReadResource("BlockedContent.html").Replace("%%MESSAGE%%", message);
		}

		internal string LoadBlockedPage(string title, string message, string backButton)
		{
			var html = ReadResource("BlockedPage.html");

			html = html.Replace("%%BACK_BUTTON%%", backButton);
			html = html.Replace("%%MESSAGE%%", message);
			html = html.Replace("%%TITLE%%", title);

			return html;
		}

		internal string LoadIsolatedClipboard()
		{
			if (isolatedClipboard == default)
			{
				isolatedClipboard = ReadResource("IsolatedClipboard.js");
			}

			return isolatedClipboard;
		}

		internal string LoadPageZoom()
		{
			if (pageZoom == default)
			{
				pageZoom = ReadResource("PageZoom.js");
			}

			return pageZoom;
		}

		private string ReadResource(string name)
		{
			var assembly = Assembly.GetAssembly(typeof(ContentLoader));
			var path = $"{typeof(ContentLoader).Namespace}.{name}";

			using (var stream = assembly.GetManifestResourceStream(path))
			using (var reader = new StreamReader(stream))
			{
				return reader.ReadToEnd();
			}
		}
	}
}
