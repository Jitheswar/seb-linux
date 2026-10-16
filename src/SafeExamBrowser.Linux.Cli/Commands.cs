/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Client;
using SafeExamBrowser.Linux.Configuration.Contracts;

namespace SafeExamBrowser.Linux.Cli
{
	internal static class Commands
	{
		public const int EXIT_SUCCESS = 0;
		public const int EXIT_USAGE = 1;
		public const int EXIT_CONFIGURATION = 2;
		public const int EXIT_RUNTIME = 3;

		public static int Check(CommandLineOptions options)
		{
			var logger = CreateLogger(options);
			var result = Load(logger, options, interactive: false);

			if (result.Status != LoadStatus.Success)
			{
				Console.Error.WriteLine(Describe(result.Status, options));
				Console.Error.WriteLine($"Configuration '{result.Source}' is NOT valid.");

				return EXIT_CONFIGURATION;
			}

			var problem = ExamSession.Validate(result.Settings);

			if (problem != default)
			{
				Console.Error.WriteLine($"Configuration '{result.Source}' is NOT valid: {problem}");

				return EXIT_CONFIGURATION;
			}

			Console.WriteLine($"Configuration '{result.Source}' is valid.");
			Console.WriteLine($"  Config key:       {result.Settings.Browser.ConfigurationKey}");

			return EXIT_SUCCESS;
		}

		public static int Info(CommandLineOptions options)
		{
			var logger = CreateLogger(options);
			var result = Load(logger, options, interactive: false);

			if (result.Status != LoadStatus.Success)
			{
				Console.Error.WriteLine(Describe(result.Status, options));

				return EXIT_CONFIGURATION;
			}

			Console.WriteLine($"Configuration: {result.Source}");
			Console.WriteLine();

			foreach (var line in ConfigurationSummary.Describe(result.Settings))
			{
				Console.WriteLine(line);
			}

			return EXIT_SUCCESS;
		}

		public static int Keys(CommandLineOptions options)
		{
			var logger = CreateLogger(options);
			var result = Load(logger, options, interactive: false);

			if (result.Status != LoadStatus.Success)
			{
				Console.Error.WriteLine(Describe(result.Status, options));

				return EXIT_CONFIGURATION;
			}

			var report = new KeyReport(result.Settings.Browser, logger);

			Console.WriteLine($"Configuration:     {result.Source}");
			Console.WriteLine($"Config Key:        {report.ConfigurationKey}");
			Console.WriteLine($"Browser Exam Key:  {report.BrowserExamKey}");

			if (!string.IsNullOrWhiteSpace(options.Url))
			{
				Console.WriteLine();
				Console.WriteLine($"Hashes for '{options.Url}':");
				Console.WriteLine($"  X-SafeExamBrowser-ConfigKeyHash: {report.ConfigurationKeyHash(options.Url)}");
				Console.WriteLine($"  X-SafeExamBrowser-RequestHash:   {report.BrowserExamKeyHash(options.Url)}");
			}

			return EXIT_SUCCESS;
		}

		public static async Task<int> RunAsync(CommandLineOptions options)
		{
			if (options.Headless)
			{
				// Must be set before the browser is launched (see BrowserLauncher.IsHeadless).
				Environment.SetEnvironmentVariable("SEB_HEADLESS", "1");
			}

			var logger = CreateLogger(options);
			var result = Load(logger, options, interactive: true);

			if (result.Status != LoadStatus.Success)
			{
				Console.Error.WriteLine(Describe(result.Status, options));

				return EXIT_CONFIGURATION;
			}

			var problem = ExamSession.Validate(result.Settings);

			if (problem != default)
			{
				Console.Error.WriteLine(problem);

				return EXIT_CONFIGURATION;
			}

			using (var cancellation = new CancellationTokenSource())
			{
				Console.CancelKeyPress += (sender, eventArgs) =>
				{
					eventArgs.Cancel = true;
					cancellation.Cancel();
				};

				var session = new ExamSession(result.Settings, new ExamSessionOptions
				{
					ChromiumExecutablePath = options.Chromium,
					KioskMode = options.NoKiosk ? false : default(bool?),
					Logger = logger,
					QuitPasswordPrompt = () => Prompt("Quit password: ")
				});

				var reason = await session.RunAsync(cancellation.Token);

				Console.Error.WriteLine($"Exam session ended ({reason}).");
			}

			return EXIT_SUCCESS;
		}

		private static ILogger CreateLogger(CommandLineOptions options)
		{
			return new ConsoleLogger("seb-linux", options.Debug);
		}

		private static string Describe(LoadStatus status, CommandLineOptions options)
		{
			switch (status)
			{
				case LoadStatus.PasswordNeeded:
					return "The configuration is password-protected, use '--password <password>' to load it!";
				case LoadStatus.NotSupported:
					return "The configuration uses an unsupported format or encryption (e.g. certificate-encrypted configurations are not supported)!";
				case LoadStatus.InvalidData:
					return "The configuration data is invalid or the file could not be retrieved!";
				default:
					return $"The configuration could not be loaded ({status})!";
			}
		}

		private static ConfigurationLoadResult Load(ILogger logger, CommandLineOptions options, bool interactive)
		{
			var loader = new ConfigurationLoader(logger);
			var result = loader.Load(options.Configuration, options.Password);

			if (result.Status == LoadStatus.PasswordNeeded && interactive && options.Password == default)
			{
				var password = Prompt("Configuration password: ");

				if (password != default)
				{
					result = loader.Load(options.Configuration, password);
				}
			}

			return result;
		}

		private static string Prompt(string message)
		{
			Console.Error.Write(message);

			return Console.ReadLine();
		}
	}
}
