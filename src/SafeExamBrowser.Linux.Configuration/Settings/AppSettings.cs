/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

namespace SafeExamBrowser.Linux.Configuration.Settings
{
	public enum LogLevel
	{
		Error = 0,
		Warning = 1,
		Info = 2,
		Debug = 3
	}

	public enum SessionMode
	{
		Normal,
		Server
	}

	public enum ConfigurationMode
	{
		Exam,
		ConfigureClient
	}

	public class AppSettings
	{
		public ApplicationSettings Applications { get; } = new ApplicationSettings();
		public BrowserSettings Browser { get; } = new BrowserSettings();
		public ConfigurationFileSettings ConfigurationFile { get; } = new ConfigurationFileSettings();
		public GeneralSettings General { get; } = new GeneralSettings();
		public KeyboardSettings Keyboard { get; } = new KeyboardSettings();
		public MouseSettings Mouse { get; } = new MouseSettings();
		public SecuritySettings Security { get; } = new SecuritySettings();
		public ServerSettings Server { get; } = new ServerSettings();
		public UserInterfaceSettings UserInterface { get; } = new UserInterfaceSettings();
	}

	public class GeneralSettings
	{
		public LogLevel LogLevel { get; set; }
		public string OriginatorVersion { get; set; }
	}

	public class ConfigurationFileSettings
	{
		public ConfigurationMode ConfigurationMode { get; set; }
		public bool KeepClientConfigurationEncryption { get; set; }
		public SessionMode SessionMode { get; set; }
	}

	public class ServerSettings
	{
		public string ApiEndpoint { get; set; }
		public string ClientName { get; set; }
		public string ClientSecret { get; set; }
		public string ExamId { get; set; }
		public string FallbackPasswordHash { get; set; }
		public string Institution { get; set; }
		public int PingInterval { get; set; }
		public bool PerformFallback { get; set; }
		public string ServerUrl { get; set; }
		public bool ShowRaiseHand { get; set; }
	}

	public class UserInterfaceSettings
	{
		public bool ShowTaskbar { get; set; }
		public bool TouchOptimized { get; set; }
	}

	public class ApplicationSettings
	{
		public System.Collections.Generic.List<ApplicationConfiguration> Whitelist { get; } = new System.Collections.Generic.List<ApplicationConfiguration>();
		public System.Collections.Generic.List<ApplicationConfiguration> Blacklist { get; } = new System.Collections.Generic.List<ApplicationConfiguration>();
	}

	public class ApplicationConfiguration
	{
		public bool Active { get; set; }
		public bool AllowCustomPath { get; set; }
		public bool AllowRunning { get; set; }
		public System.Collections.Generic.List<string> Arguments { get; } = new System.Collections.Generic.List<string>();
		public bool AutoStart { get; set; }
		public bool AutoTerminate { get; set; }
		public string Description { get; set; }
		public string DisplayName { get; set; }
		public string ExecutableName { get; set; }
		public string ExecutablePath { get; set; }
		public int? OperatingSystem { get; set; }
		public bool ShowInShell { get; set; }
		public string Signature { get; set; }
	}
}
