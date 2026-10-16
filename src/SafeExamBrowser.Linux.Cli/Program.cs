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
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Configuration.Contracts;

namespace SafeExamBrowser.Linux.Cli
{
	internal static class Program
	{
		private static async Task<int> Main(string[] args)
		{
			var (options, error) = CommandLine.Parse(args);

			if (error != default)
			{
				Console.Error.WriteLine($"Error: {error}");
				Console.Error.WriteLine();
				Console.Error.WriteLine(CommandLine.Usage);

				return Commands.EXIT_USAGE;
			}

			try
			{
				switch (options.Command)
				{
					case CommandName.Help:
						Console.WriteLine(CommandLine.Usage);
						return Commands.EXIT_SUCCESS;
					case CommandName.Version:
						Console.WriteLine($"seb-linux {AppInfo.ProgramBuildVersion} (Safe Exam Browser for Linux)");
						return Commands.EXIT_SUCCESS;
					case CommandName.Run:
						return await Commands.RunAsync(options);
					case CommandName.Info:
						return Commands.Info(options);
					case CommandName.Keys:
						return Commands.Keys(options);
					case CommandName.Check:
						return Commands.Check(options);
					default:
						Console.Error.WriteLine(CommandLine.Usage);
						return Commands.EXIT_USAGE;
				}
			}
			catch (OperationCanceledException)
			{
				Console.Error.WriteLine("Interrupted.");

				return Commands.EXIT_RUNTIME;
			}
			catch (Exception e)
			{
				Console.Error.WriteLine($"Unexpected error: {e}");

				return Commands.EXIT_RUNTIME;
			}
		}
	}
}
