/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.Linux.Cli;

namespace SafeExamBrowser.Linux.Tests
{
	public class CommandLineTests
	{
		[Fact]
		public void MustShowHelpWhenNoArgumentsGiven()
		{
			var (options, error) = CommandLine.Parse(new string[0]);

			Assert.Null(error);
			Assert.Equal(CommandName.Help, options.Command);
		}

		[Theory]
		[InlineData("help")]
		[InlineData("--help")]
		[InlineData("-h")]
		public void MustShowHelpForHelpCommand(string command)
		{
			var (options, error) = CommandLine.Parse(new[] { command });

			Assert.Null(error);
			Assert.Equal(CommandName.Help, options.Command);
		}

		[Theory]
		[InlineData("version")]
		[InlineData("--version")]
		public void MustShowVersionForVersionCommand(string command)
		{
			var (options, error) = CommandLine.Parse(new[] { command });

			Assert.Null(error);
			Assert.Equal(CommandName.Version, options.Command);
		}

		[Fact]
		public void MustParseRunCommand()
		{
			var (options, error) = CommandLine.Parse(new[] { "run", "config.seb" });

			Assert.Null(error);
			Assert.Equal(CommandName.Run, options.Command);
			Assert.Equal("config.seb", options.Configuration);
		}

		[Fact]
		public void MustParseRunCommandWithAllOptions()
		{
			var (options, error) = CommandLine.Parse(new[]
			{
				"run", "config.seb",
				"--password", "secret",
				"--chromium", "/usr/bin/chromium",
				"--headless",
				"--debug"
			});

			Assert.Null(error);
			Assert.Equal(CommandName.Run, options.Command);
			Assert.Equal("config.seb", options.Configuration);
			Assert.Equal("secret", options.Password);
			Assert.Equal("/usr/bin/chromium", options.Chromium);
			Assert.True(options.Headless);
			Assert.True(options.Debug);
		}

		[Theory]
		[InlineData("info", CommandName.Info)]
		[InlineData("keys", CommandName.Keys)]
		[InlineData("check", CommandName.Check)]
		public void MustParseOtherCommands(string command, CommandName expected)
		{
			var (options, error) = CommandLine.Parse(new[] { command, "config.seb" });

			Assert.Null(error);
			Assert.Equal(expected, options.Command);
			Assert.Equal("config.seb", options.Configuration);
		}

		[Fact]
		public void MustParseKeysCommandWithUrl()
		{
			var (options, error) = CommandLine.Parse(new[] { "keys", "config.seb", "--url", "https://exam.example.org/" });

			Assert.Null(error);
			Assert.Equal("https://exam.example.org/", options.Url);
		}

		[Fact]
		public void MustRejectSecondConfigurationArgument()
		{
			var (options, error) = CommandLine.Parse(new[] { "frobnicate", "config.seb" });

			Assert.NotNull(error);
			Assert.Equal("frobnicate", options.Configuration);
			Assert.Contains("config.seb", error);
		}

		[Fact]
		public void MustRejectMissingConfiguration()
		{
			var (_, error) = CommandLine.Parse(new[] { "run" });

			Assert.NotNull(error);
			Assert.Contains("configuration", error);
		}

		[Fact]
		public void MustRejectMissingOptionValue()
		{
			var (_, error) = CommandLine.Parse(new[] { "run", "config.seb", "--password" });

			Assert.NotNull(error);
			Assert.Contains("--password", error);
		}

		[Fact]
		public void MustRejectUnknownOption()
		{
			var (_, error) = CommandLine.Parse(new[] { "run", "config.seb", "--kiosk" });

			Assert.NotNull(error);
			Assert.Contains("--kiosk", error);
		}

		[Fact]
		public void MustRejectUnexpectedArguments()
		{
			var (_, error) = CommandLine.Parse(new[] { "run", "config.seb", "another.seb" });

			Assert.NotNull(error);
			Assert.Contains("another.seb", error);
		}
	}
}
