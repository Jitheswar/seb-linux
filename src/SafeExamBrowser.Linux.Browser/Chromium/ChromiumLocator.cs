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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

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

		// Windows installation layout, relative to PROGRAMFILES / PROGRAMFILES(X86) / LOCALAPPDATA.
		private static readonly string[] WINDOWS_INSTALLATION_PATHS =
		{
			"Google\\Chrome\\Application\\chrome.exe",
			"Chromium\\Application\\chrome.exe",
			"BraveSoftware\\Brave-Browser\\Application\\brave.exe"
		};

		/// <summary>
		/// Finds the Chromium executable, honoring the SEB_CHROMIUM environment variable. If multiple
		/// browsers are found, the newest one is preferred, because the DevTools features required for
		/// the exam key injection are only available in recent versions. Returns null if no executable is available.
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

			var candidates = new List<string>();

			void Add(string path)
			{
				if (path != default && File.Exists(path) && !candidates.Contains(path))
				{
					candidates.Add(path);
				}
			}

			foreach (var name in EXECUTABLE_NAMES)
			{
				Add(FindOnPath(name));
			}

			foreach (var directory in COMMON_PATHS)
			{
				foreach (var name in EXECUTABLE_NAMES)
				{
					Add(Path.Combine(directory, name));
				}
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				foreach (var root in new[]
					{
						Environment.GetEnvironmentVariable("PROGRAMFILES"),
						Environment.GetEnvironmentVariable("PROGRAMFILES(X86)"),
						Environment.GetEnvironmentVariable("LOCALAPPDATA")
					})
				{
					if (string.IsNullOrWhiteSpace(root))
					{
						continue;
					}

					foreach (var relative in WINDOWS_INSTALLATION_PATHS)
					{
						Add(Path.Combine(root, relative));
					}
				}
			}

			AddChromeForTesting(Add);

			if (candidates.Count == 0)
			{
				return default;
			}

			return candidates.OrderByDescending(c => GetVersionMajor(GetVersion(c))).First();
		}

		/// <summary>
		/// Adds the browser of a "Chrome for Testing" installation (e.g. from npx @puppeteer/browsers or Playwright).
		/// </summary>
		private static void AddChromeForTesting(Action<string> add)
		{
			var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".chromium", "chrome");
			string[] versions;

			try
			{
				versions = Directory.Exists(baseDirectory) ? Directory.GetDirectories(baseDirectory) : default;
			}
			catch (Exception)
			{
				return;
			}

			var relative = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? "chrome-win64\\chrome.exe"
				: "chrome-linux64/chrome";

			foreach (var version in versions ?? Array.Empty<string>())
			{
				add(Path.Combine(version, relative));
			}
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

		/// <summary>
		/// Determines the major version number (e.g. 120 for "120.0.6099.71") of the given version string, or 0 if it cannot be determined.
		/// </summary>
		internal static int GetVersionMajor(string version)
		{
			if (!string.IsNullOrWhiteSpace(version) && int.TryParse(version.Split('.')[0], out var major))
			{
				return major;
			}

			return 0;
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

					if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					{
						candidate = Path.Combine(directory.Trim(), name + ".exe");

						if (File.Exists(candidate))
						{
							return candidate;
						}
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
