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
