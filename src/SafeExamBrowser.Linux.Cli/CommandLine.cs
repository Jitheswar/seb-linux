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

namespace SafeExamBrowser.Linux.Cli
{
	public enum CommandName
	{
		None,
		Help,
		Version,
		Run,
		Info,
		Keys,
		Check
	}

	public sealed class CommandLineOptions
	{
		public CommandName Command { get; set; }
		public string Chromium { get; set; }
		public string Configuration { get; set; }
		public bool Debug { get; set; }
		public bool Headless { get; set; }
		public string Password { get; set; }
		public string Url { get; set; }
	}

	public static class CommandLine
	{
		public const string Usage = @"Exam Unlock (Safe Exam Browser)

Usage:
  exam-unlock <command> <configuration> [options]

Commands:
  run     Start an exam session with the given configuration
  info    Show the settings of the given configuration
  keys    Show the config key and browser exam key of the given configuration
  check   Validate the given configuration
  version Show the program version
  help    Show this help

The configuration may be a file path, an http(s) URL or a data URI of a
SEB configuration file (.seb).

Options:
  --password <password>   The password for an encrypted configuration
  --headless              Launch the browser without a display server (testing)
  --chromium <path>       The Chromium executable to be used
  --url <url>             The URL for which the request hashes are to be shown
  --debug                 Enable debug logging

Exit codes:
  0  success
  1  invalid usage
  2  configuration error
  3  runtime error";

		public static (CommandLineOptions Options, string Error) Parse(string[] args)
		{
			var options = new CommandLineOptions();

			if (args == default(string[]) || args.Length == 0)
			{
				options.Command = CommandName.Help;

				return (options, default);
			}

			var command = args[0].ToLowerInvariant();

			switch (command)
			{
				case "help":
				case "--help":
				case "-h":
					options.Command = CommandName.Help;
					return (options, default);
				case "version":
				case "--version":
					options.Command = CommandName.Version;
					return (options, default);
				case "run":
					options.Command = CommandName.Run;
					break;
				case "info":
					options.Command = CommandName.Info;
					break;
				case "keys":
					options.Command = CommandName.Keys;
					break;
				case "check":
					options.Command = CommandName.Check;
					break;
				default:
					return (options, $"Unknown command '{args[0]}'!");
			}

			for (var index = 1; index < args.Length; index++)
			{
				var argument = args[index];

				switch (argument)
				{
					case "--password":
						if (!TryTakeValue(args, ref index, out var password))
						{
							return (options, "Missing value for option '--password'!");
						}

						options.Password = password;
						break;
					case "--url":
						if (!TryTakeValue(args, ref index, out var url))
						{
							return (options, "Missing value for option '--url'!");
						}

						options.Url = url;
						break;
					case "--chromium":
						if (!TryTakeValue(args, ref index, out var chromium))
						{
							return (options, "Missing value for option '--chromium'!");
						}

						options.Chromium = chromium;
						break;
					case "--headless":
						options.Headless = true;
						break;
					case "--debug":
						options.Debug = true;
						break;
					default:
						if (argument.StartsWith("--", StringComparison.Ordinal))
						{
							return (options, $"Unknown option '{argument}'!");
						}

						if (options.Configuration != default)
						{
							return (options, $"Unexpected argument '{argument}'!");
						}

						options.Configuration = argument;
						break;
				}
			}

			if (options.Configuration == default)
			{
				return (options, $"The '{command}' command requires a configuration file or URL!");
			}

			return (options, default);
		}

		private static bool TryTakeValue(string[] args, ref int index, out string value)
		{
			value = default;

			if (index + 1 >= args.Length)
			{
				return false;
			}

			value = args[++index];

			return true;
		}
	}
}
