/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (replaces the CEF runtime of the Windows version).
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace SafeExamBrowser.Linux.Browser.Chromium
{
	/// <summary>
	/// Locates the system Chromium-based browser used to display the exam.
	/// </summary>
	internal static class ChromiumLocator
	{
		private static readonly string[] EXECUTABLE_NAMES =
		{
			"chromium",
			"chromium-browser",
			"google-chrome-stable",
			"google-chrome",
			"brave-browser"
		};

		private static readonly string[] COMMON_PATHS =
		{
			"/usr/bin",
			"/usr/local/bin",
			"/snap/bin",
			"/opt/google/chrome",
			"/var/lib/flatpak/exports/bin"
		};

		/// <summary>
		/// Finds the Chromium executable, honoring the SEB_CHROMIUM environment variable. Returns null if no executable is available.
		/// </summary>
		internal static string Find()
		{
			var configured = Environment.GetEnvironmentVariable("SEB_CHROMIUM");

			if (!string.IsNullOrWhiteSpace(configured))
			{
				if (File.Exists(configured))
				{
					return configured;
				}

				Console.Error.WriteLine($"WARN  | Browser | The configured Chromium executable '{configured}' does not exist.");
			}

			foreach (var name in EXECUTABLE_NAMES)
			{
				var path = FindOnPath(name);

				if (path != default)
				{
					return path;
				}
			}

			foreach (var directory in COMMON_PATHS)
			{
				foreach (var name in EXECUTABLE_NAMES)
				{
					var path = Path.Combine(directory, name);

					if (File.Exists(path))
					{
						return path;
					}
				}
			}

			return default;
		}

		/// <summary>
		/// Determines the Chromium version (e.g. "120.0.6099.71") of the given executable, or null if it cannot be determined.
		/// </summary>
		internal static string GetVersion(string executablePath)
		{
			try
			{
				using (var process = Process.Start(new ProcessStartInfo
				{
					FileName = executablePath,
					Arguments = "--version",
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false
				}))
				{
					var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

					process.WaitForExit(5000);

					var match = Regex.Match(output, @"(\d+\.\d+\.\d+\.\d+)");

					if (match.Success)
					{
						return match.Groups[1].Value;
					}
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"WARN  | Browser | Could not determine Chromium version: {e.Message}");
			}

			return default;
		}

		private static string FindOnPath(string name)
		{
			var path = Environment.GetEnvironmentVariable("PATH");

			if (string.IsNullOrWhiteSpace(path))
			{
				return default;
			}

			foreach (var directory in path.Split(Path.PathSeparator))
			{
				if (string.IsNullOrWhiteSpace(directory))
				{
					continue;
				}

				try
				{
					var candidate = Path.Combine(directory.Trim(), name);

					if (File.Exists(candidate))
					{
						return candidate;
					}
				}
				catch (ArgumentException)
				{
				}
			}

			return default;
		}
	}
}
