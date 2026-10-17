/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (replaces the CEF runtime of the Windows version).
 * The command line flags are ported from the CEF settings of the Windows version (see
 * Browser/Responsibilities/Browser/ConfigurationResponsibility.cs).
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Browser.Chromium
{
	public class BrowserLaunchOptions
	{
		/// <summary>
		/// The executable of the Chromium-based browser to be used, or null to auto-detect.
		/// </summary>
		public string ExecutablePath { get; set; }

		/// <summary>
		/// The directory to be used as Chromium user data directory, or null to use a temporary directory.
		/// </summary>
		public string ProfileDirectory { get; set; }

		/// <summary>
		/// The user agent to be used, or null to use the default user agent.
		/// </summary>
		public string UserAgent { get; set; }

		/// <summary>
		/// The file to which the browser output is to be logged, or null to discard it.
		/// </summary>
		public string LogFile { get; set; }

		public BrowserSettings Settings { get; set; }
	}

	public sealed class BrowserLaunchResult
	{
		public string ExecutablePath { get; set; }
		public int ProcessId { get; set; }
		public string ProfileDirectory { get; set; }
		public string UserAgent { get; set; }
		public string WebSocketUrl { get; set; }
	}

	/// <summary>
	/// Launches the system Chromium with a remote debugging endpoint and waits until it can be controlled.
	/// </summary>
	internal sealed class BrowserLauncher : IDisposable
	{
		private readonly ILogger logger;
		private readonly List<Task> outputReaders = new List<Task>();
		private Process process;

		internal BrowserLauncher(ILogger logger)
		{
			this.logger = logger;
		}

		internal async Task<BrowserLaunchResult> LaunchAsync(BrowserLaunchOptions options, CancellationToken cancellationToken)
		{
			var executable = options.ExecutablePath ?? ChromiumLocator.Find();

			if (executable == default)
			{
				throw new InvalidOperationException(
					"Could not find a Chromium-based browser! Install Chromium (e.g. 'apt install chromium') " +
					"or set the SEB_CHROMIUM environment variable to the browser executable.");
			}

			var majorVersion = ChromiumLocator.GetVersionMajor(ChromiumLocator.GetVersion(executable));

			if (majorVersion > 0 && majorVersion < 90)
			{
				throw new InvalidOperationException($"The Chromium-based browser '{executable}' (version {majorVersion}) is too old: " +
					"Safe Exam Browser requires Chromium 90 or newer (headless mode and CDP request interception). " +
					"Install a current Chromium or Google Chrome, or point the SEB_CHROMIUM environment variable at one.");
			}

			var profileDirectory = options.ProfileDirectory ?? Path.Combine(Path.GetTempPath(), $"SafeExamBrowser-{Guid.NewGuid():N}");
			var userAgent = options.UserAgent ?? BuildUserAgent(executable, options.Settings);
			var arguments = BuildArguments(options, profileDirectory, userAgent);

			Directory.CreateDirectory(profileDirectory);

			// Any endpoint file left over from a previous session must be removed, as it would
			// otherwise be picked up as the endpoint of the current session (the browser only
			// replaces the file once its debugging server has been started).
			try
			{
				File.Delete(Path.Combine(profileDirectory, "DevToolsActivePort"));
			}
			catch (Exception e)
			{
				logger.Warn($"Could not remove the stale DevTools endpoint file from '{profileDirectory}': {e.Message}");
			}

			logger.Info($"Launching browser '{executable}' with profile '{profileDirectory}'.");
			logger.Debug($"Browser arguments: {string.Join(" ", arguments)}");

			var startInfo = new ProcessStartInfo
			{
				FileName = executable,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				WorkingDirectory = Path.GetTempPath()
			};

			// A stale D-Bus socket can outlive its bus daemon. When Chrome consults such a bus,
			// every network request hangs (the bus call never completes), which makes pages fail
			// to load. The exam browser has no use for D-Bus, so it must not depend on it.
			startInfo.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
			startInfo.Environment.Remove("DBUS_SYSTEM_BUS_ADDRESS");
			foreach (var argument in arguments)
			{
				startInfo.ArgumentList.Add(argument);
			}

			process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
			process.Start();

			ReadOutput(process.StandardOutput, options.LogFile);
			ReadOutput(process.StandardError, options.LogFile);

			var webSocketUrl = await WaitForDevToolsAsync(profileDirectory, process, cancellationToken);

			logger.Info($"Browser ready (process {process.Id}), DevTools endpoint: {webSocketUrl}.");

			return new BrowserLaunchResult
			{
				ExecutablePath = executable,
				ProcessId = process.Id,
				ProfileDirectory = profileDirectory,
				UserAgent = userAgent,
				WebSocketUrl = webSocketUrl
			};
		}

		/// <summary>
		/// Builds the user agent in the same way as the Windows version
		/// ("Mozilla/5.0 (Windows NT &lt;version&gt;) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/&lt;version&gt; SEB/&lt;version&gt;"),
		/// with the platform token matching the operating system the client runs on.
		/// </summary>
		internal static string BuildUserAgent(string executable, BrowserSettings settings)
		{
			var chromiumVersion = ChromiumLocator.GetVersion(executable) ?? "unknown";
			var sebVersion = $"SEB/{AppInfo.ProgramBuildVersion}";
			var userAgent = default(string);

			if (!string.IsNullOrWhiteSpace(settings?.CustomUserAgent))
			{
				userAgent = $"{settings.CustomUserAgent} {sebVersion}";
			}
			else
			{
				var platform = OperatingSystem.IsWindows()
					? $"Windows NT {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}"
					: "X11; Linux x86_64";

				userAgent = $"Mozilla/5.0 ({platform}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromiumVersion} {sebVersion}";
			}

			if (!string.IsNullOrWhiteSpace(settings?.UserAgentSuffix))
			{
				userAgent = $"{userAgent} {settings.UserAgentSuffix}";
			}

			return userAgent;
		}

		/// <summary>
		/// Terminates the browser process if it is still running.
		/// </summary>
		internal void Terminate()
		{
			try
			{
				if (process != default && !process.HasExited)
				{
					logger.Info("Terminating browser process...");

					process.Kill(entireProcessTree: true);
					process.WaitForExit(10000);
				}
			}
			catch (Exception e)
			{
				logger.Error("Could not terminate browser process!", e);
			}
		}

		/// <summary>
		/// Waits until the browser process has exited.
		/// </summary>
		internal Task WaitForExitAsync(CancellationToken cancellationToken)
		{
			return process == default ? Task.CompletedTask : process.WaitForExitAsync(cancellationToken);
		}

		/// <summary>
		/// Deletes the user data directory, e.g. when the browser profile is not to be preserved.
		/// </summary>
		internal static void DeleteProfile(string profileDirectory)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(profileDirectory) && Directory.Exists(profileDirectory))
				{
					Directory.Delete(profileDirectory, recursive: true);
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"WARN  | Browser | Could not delete the browser profile '{profileDirectory}': {e.Message}");
			}
		}

		public void Dispose()
		{
			Terminate();
			process?.Dispose();
		}

		private List<string> BuildArguments(BrowserLaunchOptions options, string profileDirectory, string userAgent)
		{
			var settings = options.Settings ?? new BrowserSettings();
			var headless = IsHeadless();
			var arguments = new List<string>
			{
				$"--user-data-dir={profileDirectory}",
				"--remote-debugging-port=0",
				"--remote-allow-origins=*",

				"--no-first-run",
				"--no-default-browser-check",
				"--noerrdialogs",
				"--disable-infobars",
				"--hide-crash-restore-bubble",
				"--disable-session-crashed-bubble",
				"--disable-features=Translate,MediaRouter,OptimizationHints",
				"--disable-translate",

				"--password-store=basic",
				"--enable-media-stream",
				"--enable-usermedia-screen-capturing",
				"--enable-features=WebRTCPipeWireCapturer",
				"--use-fake-ui-for-media-stream",
				"--autoplay-policy=no-user-gesture-required",

				"--disable-extensions",
				"--disable-component-update",
				"--disable-background-networking",

				// Keep the exam page at full speed (timers, rendering) even when the window is
				// not in focus or is covered by another window.
				"--disable-background-timer-throttling",
				"--disable-backgrounding-occluded-windows",
				"--disable-renderer-backgrounding",

				$"--user-agent={userAgent}"
			};

			var language = CultureInfo.CurrentUICulture.Name;

			if (!string.IsNullOrWhiteSpace(language))
			{
				arguments.Add($"--accept-lang={language}");
				arguments.Add($"--lang={language.Split('-')[0]}");
			}

			if (headless)
			{
				// Headless operation is used for tests and in environments without a display server.
				arguments.Add("--headless=new");
				arguments.Add("--window-size=1280,800");
			}
			else
			{
				arguments.Add("--app=about:blank");
				AddWindowGeometry(arguments, settings.MainWindow);
			}

			arguments.Add("--touch-events=enabled");

			if (!settings.AllowSpellChecking)
			{
				arguments.Add("--disable-spell-checking");
			}

			if (Environment.UserName == "root")
			{
				// Chromium refuses to start its sandbox when running as root (e.g. in containers).
				arguments.Add("--no-sandbox");
			}

			arguments.Add("about:blank");

			return arguments;
		}

		private void AddWindowGeometry(List<string> arguments, MainWindowSettings window)
		{
			var width = ParsePixelValue(window?.WindowWidth);
			var height = ParsePixelValue(window?.WindowHeight);

			if (width > 0 && height > 0)
			{
				arguments.Add($"--window-size={width},{height}");
			}
			else
			{
				arguments.Add("--start-maximized");
			}

			var position = window?.WindowPosition;

			if (!string.IsNullOrWhiteSpace(position) && position.Contains(","))
			{
				arguments.Add($"--window-position={position.Replace(" ", "")}");
			}
		}

		/// <summary>
		/// Determines whether the browser is to be launched without a display server, either
		/// because SEB_HEADLESS is set to 1 or because no display server is available.
		/// </summary>
		private static bool IsHeadless()
		{
			if (Environment.GetEnvironmentVariable("SEB_HEADLESS") == "1")
			{
				return true;
			}

			return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
				&& string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
		}

		private int ParsePixelValue(string value)
		{
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixels) && pixels > 100)
			{
				return pixels;
			}

			return 0;
		}

		private async Task<string> WaitForDevToolsAsync(string profileDirectory, Process process, CancellationToken cancellationToken)
		{
			var file = Path.Combine(profileDirectory, "DevToolsActivePort");
			var timeout = DateTime.UtcNow.AddSeconds(30);
			var delay = 50;

			while (DateTime.UtcNow < timeout)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (process.HasExited)
				{
					throw new InvalidOperationException($"The browser process exited unexpectedly with exit code {process.ExitCode}!");
				}

				if (File.Exists(file))
				{
					try
					{
						var lines = File.ReadAllLines(file);

						if (lines.Length >= 2 && int.TryParse(lines[0], out var port))
						{
							return $"ws://127.0.0.1:{port}{lines[1]}";
						}
					}
					catch (IOException)
					{
						// The file is still being written, retry.
					}
				}

				await Task.Delay(delay, cancellationToken);

				delay = Math.Min(delay * 2, 500);
			}

			throw new TimeoutException($"The browser did not provide a DevTools endpoint within 30 seconds (profile '{profileDirectory}')!");
		}

		private void ReadOutput(StreamReader reader, string logFile)
		{
			outputReaders.Add(Task.Run(async () =>
			{
				try
				{
					StreamWriter writer = default;

					if (!string.IsNullOrWhiteSpace(logFile))
					{
						writer = new StreamWriter(logFile, append: true) { AutoFlush = true };
					}

					try
					{
						string line;

						while ((line = await reader.ReadLineAsync()) != default)
						{
							writer?.WriteLine(line);
						}
					}
					finally
					{
						writer?.Dispose();
					}
				}
				catch (Exception)
				{
				}
			}));
		}
	}
}
