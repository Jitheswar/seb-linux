/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Configuration.ConfigurationData
{
	/// <summary>
	/// Maps the raw configuration data (settings key/value dictionary) to the strongly typed settings model.
	/// Keys which are not (yet) supported on Linux are intentionally ignored.
	/// </summary>
	internal class DataMapper
	{
		internal void Map(IDictionary<string, object> data, AppSettings settings)
		{
			MapApplications(data, settings);
			MapBrowser(data, settings);
			MapConfigurationFile(data, settings);
			MapGeneral(data, settings);
			MapKeyboard(data, settings);
			MapMouse(data, settings);
			MapSecurity(data, settings);
			MapServer(data, settings);
			MapUserInterface(data, settings);
		}

		private void MapApplications(IDictionary<string, object> data, AppSettings settings)
		{
			MapApplicationList(data, Keys.Applications.Whitelist, settings.Applications.Whitelist);
			MapApplicationList(data, Keys.Applications.Blacklist, settings.Applications.Blacklist);
		}

		private void MapApplicationList(IDictionary<string, object> data, string key, List<ApplicationConfiguration> list)
		{
			if (data.TryGetValue(key, out var value) && value is IList<object> items)
			{
				foreach (var item in items)
				{
					if (item is IDictionary<string, object> entry)
					{
						var application = new ApplicationConfiguration();

						application.Active = entry.TryGetValue(Keys.Applications.Active, out var v) && v is bool active && active;
						application.AllowCustomPath = entry.TryGetValue(Keys.Applications.AllowCustomPath, out v) && v is bool allowPath && allowPath;
						application.AllowRunning = entry.TryGetValue(Keys.Applications.AllowRunning, out v) && v is bool allowRun && allowRun;
						application.AutoStart = entry.TryGetValue(Keys.Applications.AutoStart, out v) && v is bool autoStart && autoStart;
						application.AutoTerminate = entry.TryGetValue(Keys.Applications.AutoTerminate, out v) && v is bool kill && kill;
						application.Description = entry.TryGetValue(Keys.Applications.Description, out v) ? v as string : null;
						application.DisplayName = entry.TryGetValue(Keys.Applications.DisplayName, out v) ? v as string : null;
						application.ExecutableName = entry.TryGetValue(Keys.Applications.ExecutableName, out v) ? v as string : null;
						application.ExecutablePath = entry.TryGetValue(Keys.Applications.ExecutablePath, out v) ? v as string : null;
						application.OperatingSystem = entry.TryGetValue(Keys.Applications.OperatingSystem, out v) && v is int os ? os : default(int?);
						application.ShowInShell = entry.TryGetValue(Keys.Applications.ShowInShell, out v) && v is bool shell && shell;
						application.Signature = entry.TryGetValue(Keys.Applications.Signature, out v) ? v as string : null;

						if (entry.TryGetValue(Keys.Applications.Arguments, out v) && v is IList<object> arguments)
						{
							foreach (var argument in arguments)
							{
								application.Arguments.Add(argument as string);
							}
						}

						// Only applications which are active and have a name are useful for monitoring.
						if (application.Active && !string.IsNullOrWhiteSpace(application.ExecutableName))
						{
							list.Add(application);
						}
					}
				}
			}
		}

		private void MapBrowser(IDictionary<string, object> data, AppSettings settings)
		{
			SetBool(data, Keys.Browser.AllowConfigurationDownloads, v => settings.Browser.AllowConfigurationDownloads = v);
			SetBool(data, Keys.Browser.AllowCustomDownUploadLocation, v => settings.Browser.AllowCustomDownUploadLocation = v);
			SetBool(data, Keys.Browser.AllowDownloads, v => settings.Browser.AllowDownloads = v);
			SetBool(data, Keys.Browser.AllowFind, v => settings.Browser.AllowFind = v);
			SetBool(data, Keys.Browser.AllowPageZoom, v => settings.Browser.AllowPageZoom = v);
			SetBool(data, Keys.Browser.AllowPdfReaderToolbar, v => settings.Browser.AllowPdfReaderToolbar = v);
			SetBool(data, Keys.Browser.AllowPrint, v => settings.Browser.AllowPrint = v);
			SetBool(data, Keys.Browser.AllowSpellChecking, v => settings.Browser.AllowSpellChecking = v);
			SetBool(data, Keys.Browser.AllowUploads, v => settings.Browser.AllowUploads = v);
			SetBool(data, Keys.Browser.DeleteCacheOnShutdown, v => settings.Browser.DeleteCacheOnShutdown = v);
			SetBool(data, Keys.Browser.DeleteCookiesOnShutdown, v => settings.Browser.DeleteCookiesOnShutdown = v);
			SetBool(data, Keys.Browser.DeleteCookiesOnStartup, v => settings.Browser.DeleteCookiesOnStartup = v);
			SetBool(data, Keys.Browser.DownloadPdfFiles, v => settings.Browser.DownloadPdfFiles = v);
			SetBool(data, Keys.Browser.EnableBrowser, v => settings.Browser.EnableBrowser = v);
			SetBool(data, Keys.Browser.QuitUrlConfirmation, v => settings.Browser.QuitUrlConfirmation = v);
			SetBool(data, Keys.Browser.ResetOnQuitUrl, v => settings.Browser.ResetOnQuitUrl = v);
			SetBool(data, Keys.Browser.ShowReloadButton, v => settings.Browser.MainWindow.ShowReloadButton = v);
			SetBool(data, Keys.Browser.UseQueryParameter, v => settings.Browser.UseQueryParameter = v);
			SetBool(data, Keys.Browser.UseTemporaryDownUploadDirectory, v => settings.Browser.UseTemporaryDownUploadDirectory = v);

			SetString(data, Keys.Browser.CustomBrowserExamKey, v => settings.Browser.CustomBrowserExamKey = v);
			SetString(data, Keys.Browser.DownloadDirectory, v => settings.Browser.DownloadDirectory = v);
			SetString(data, Keys.Browser.HomeButtonMessage, v => settings.Browser.HomeButtonMessage = v);
			SetString(data, Keys.Browser.HomeButtonUrl, v => settings.Browser.HomeUrl = v);
			SetString(data, Keys.Browser.QuitUrl, v => settings.Browser.QuitUrl = v);
			SetString(data, Keys.Browser.StartUrl, v => settings.Browser.StartUrl = v);
			SetString(data, Keys.Browser.UserAgentSuffix, v => settings.Browser.UserAgentSuffix = v);

			SetBool(data, Keys.Browser.HomeButtonRequiresPassword, v => settings.Browser.HomeButtonRequiresPassword = v);
			SetBool(data, Keys.Browser.HomeButtonUseStartUrl, v => settings.Browser.UseStartUrlAsHomeUrl = v);

			if (data.TryGetValue(Keys.Browser.ExamKeySalt, out var salt) && salt is byte[] saltBytes)
			{
				settings.Browser.BrowserExamKeySalt = saltBytes;
			}

			if (data.TryGetValue(Keys.Browser.SendCustomHeaders, out var send) && send is bool sendKeys)
			{
				settings.Browser.SendBrowserExamKey = sendKeys;
				settings.Browser.SendConfigurationKey = sendKeys;
			}

			MapUserAgent(data, settings);

			// Main browser window.
			SetBool(data, Keys.Browser.MainWindow.AllowAddressBar, v => settings.Browser.MainWindow.AllowAddressBar = v);
			SetBool(data, Keys.Browser.MainWindow.AllowNavigation, v => settings.Browser.MainWindow.AllowNavigation = v);
			SetBool(data, Keys.Browser.MainWindow.AllowReload, v => settings.Browser.MainWindow.AllowReloading = v);
			SetBool(data, Keys.Browser.MainWindow.ShowReloadWarning, v => settings.Browser.MainWindow.ShowReloadWarning = v);
			SetBool(data, Keys.Browser.ShowToolbar, v => settings.Browser.MainWindow.ShowToolbar = v);
			SetString(data, Keys.Browser.MainWindow.WindowHeight, v => settings.Browser.MainWindow.WindowHeight = v);
			SetString(data, Keys.Browser.MainWindow.WindowWidth, v => settings.Browser.MainWindow.WindowWidth = v);
			SetString(data, Keys.Browser.MainWindow.WindowPosition, v => settings.Browser.MainWindow.WindowPosition = v);

			if (data.TryGetValue(Keys.Browser.MainWindow.UrlPolicy, out var mainUrlPolicy) && mainUrlPolicy is int mainPolicy)
			{
				settings.Browser.MainWindow.UrlPolicy = MapUrlPolicy(mainPolicy);
			}

			if (data.TryGetValue(Keys.Browser.MainWindow.WindowMode, out var windowMode) && windowMode is int mode)
			{
				settings.Browser.MainWindow.WindowMode = mode == 1 ? WindowMode.Fullscreen : WindowMode.Windowed;
			}

			// Additional browser window.
			SetBool(data, Keys.Browser.AdditionalWindow.AllowAddressBar, v => settings.Browser.AdditionalWindow.AllowAddressBar = v);
			SetBool(data, Keys.Browser.AdditionalWindow.AllowNavigation, v => settings.Browser.AdditionalWindow.AllowNavigation = v);
			SetBool(data, Keys.Browser.AdditionalWindow.AllowReload, v => settings.Browser.AdditionalWindow.AllowReloading = v);
			SetBool(data, Keys.Browser.AdditionalWindow.ShowReloadWarning, v => settings.Browser.AdditionalWindow.ShowReloadWarning = v);
			SetString(data, Keys.Browser.AdditionalWindow.WindowHeight, v => settings.Browser.AdditionalWindow.WindowHeight = v);
			SetString(data, Keys.Browser.AdditionalWindow.WindowWidth, v => settings.Browser.AdditionalWindow.WindowWidth = v);
			SetString(data, Keys.Browser.AdditionalWindow.WindowPosition, v => settings.Browser.AdditionalWindow.WindowPosition = v);

			if (data.TryGetValue(Keys.Browser.AdditionalWindow.UrlPolicy, out var additionalUrlPolicy) && additionalUrlPolicy is int additionalPolicy)
			{
				settings.Browser.AdditionalWindow.UrlPolicy = MapUrlPolicy(additionalPolicy);
			}

			MapUrlFilter(data, settings);
		}

		private void MapUrlFilter(IDictionary<string, object> data, AppSettings settings)
		{
			SetBool(data, Keys.Browser.Filter.EnableMainRequestFilter, v => settings.Browser.Filter.EnableMainRequestFilter = v);
			SetBool(data, Keys.Browser.Filter.EnableContentRequestFilter, v => settings.Browser.Filter.EnableContentRequestFilter = v);

			var globalRegex = data.TryGetValue("urlFilterRegex", out var regexValue) && regexValue is bool globalRegexValue && globalRegexValue;

			if (data.TryGetValue(Keys.Browser.Filter.FilterRules, out var value) && value is IList<object> rules)
			{
				foreach (var item in rules)
				{
					if (item is IDictionary<string, object> ruleData)
					{
						var isActive = ruleData.TryGetValue(Keys.Browser.Filter.RuleIsActive, out var v) && v is bool active && active;

						if (isActive)
						{
							var rule = new UrlFilterRule
							{
								Action = ruleData.TryGetValue(Keys.Browser.Filter.RuleAction, out v) && v is int action && action == 1,
								IsActive = true,
								Expression = ruleData.TryGetValue(Keys.Browser.Filter.RuleExpression, out v) ? v as string : null,
								ExpressionIsRegex = globalRegex || (ruleData.TryGetValue(Keys.Browser.Filter.RuleExpressionIsRegex, out v) && v is bool isRegex && isRegex)
							};

							if (!string.IsNullOrWhiteSpace(rule.Expression))
							{
								settings.Browser.Filter.Rules.Add(rule);
							}
						}
					}
				}
			}
		}

		private UrlPolicy MapUrlPolicy(int value)
		{
			return value switch
			{
				3 => UrlPolicy.Always,
				2 => UrlPolicy.BeforeTitle,
				1 => UrlPolicy.LoadError,
				_ => UrlPolicy.Never
			};
		}

		private void MapUserAgent(IDictionary<string, object> data, AppSettings settings)
		{
			const int DEFAULT = 0;

			if (data.TryGetValue(Keys.Browser.UserAgentModeDesktop, out var mode) && mode is int desktopMode && desktopMode != DEFAULT)
			{
				settings.Browser.CustomUserAgent = data.TryGetValue(Keys.Browser.CustomUserAgentDesktop, out var custom) ? custom as string : null;
			}
		}

		private void MapConfigurationFile(IDictionary<string, object> data, AppSettings settings)
		{
			if (data.TryGetValue(Keys.ConfigurationFile.ConfigurationPurpose, out var purpose) && purpose is int mode)
			{
				settings.ConfigurationFile.ConfigurationMode = mode == 1 ? ConfigurationMode.ConfigureClient : ConfigurationMode.Exam;
			}

			if (data.TryGetValue(Keys.ConfigurationFile.KeepClientConfigEncryption, out var keep) && keep is bool keepEncryption)
			{
				settings.ConfigurationFile.KeepClientConfigurationEncryption = keepEncryption;
			}

			if (data.TryGetValue(Keys.ConfigurationFile.SessionMode, out var session) && session is int sessionMode)
			{
				settings.ConfigurationFile.SessionMode = sessionMode == 1 ? SessionMode.Server : SessionMode.Normal;
			}
		}

		private void MapGeneral(IDictionary<string, object> data, AppSettings settings)
		{
			if (data.TryGetValue(Keys.General.LogLevel, out var level) && level is int logLevel)
			{
				settings.General.LogLevel = logLevel switch
				{
					0 => LogLevel.Error,
					1 => LogLevel.Warning,
					2 => LogLevel.Info,
					_ => LogLevel.Debug
				};
			}

			if (data.TryGetValue(Keys.General.OriginatorVersion, out var version) && version is string originator)
			{
				settings.General.OriginatorVersion = originator;
			}
		}

		private void MapKeyboard(IDictionary<string, object> data, AppSettings settings)
		{
			SetBool(data, Keys.Keyboard.EnableAltEsc, v => settings.Keyboard.AllowAltEsc = v);
			SetBool(data, Keys.Keyboard.EnableAltF4, v => settings.Keyboard.AllowAltF4 = v);
			SetBool(data, Keys.Keyboard.EnableAltTab, v => settings.Keyboard.AllowAltTab = v);
			SetBool(data, Keys.Keyboard.EnableCtrlEsc, v => settings.Keyboard.AllowCtrlEsc = v);
			SetBool(data, Keys.Keyboard.EnableEsc, v => settings.Keyboard.AllowEsc = v);
			SetBool(data, Keys.Keyboard.EnablePrintScreen, v => settings.Keyboard.AllowPrintScreen = v);
			SetBool(data, Keys.Keyboard.EnableSystemKey, v => settings.Keyboard.AllowSystemKey = v);

			SetBool(data, Keys.Keyboard.EnableF1, v => settings.Keyboard.AllowFunctionKeys[1] = v);
			SetBool(data, Keys.Keyboard.EnableF2, v => settings.Keyboard.AllowFunctionKeys[2] = v);
			SetBool(data, Keys.Keyboard.EnableF3, v => settings.Keyboard.AllowFunctionKeys[3] = v);
			SetBool(data, Keys.Keyboard.EnableF4, v => settings.Keyboard.AllowFunctionKeys[4] = v);
			SetBool(data, Keys.Keyboard.EnableF5, v => settings.Keyboard.AllowFunctionKeys[5] = v);
			SetBool(data, Keys.Keyboard.EnableF6, v => settings.Keyboard.AllowFunctionKeys[6] = v);
			SetBool(data, Keys.Keyboard.EnableF7, v => settings.Keyboard.AllowFunctionKeys[7] = v);
			SetBool(data, Keys.Keyboard.EnableF8, v => settings.Keyboard.AllowFunctionKeys[8] = v);
			SetBool(data, Keys.Keyboard.EnableF9, v => settings.Keyboard.AllowFunctionKeys[9] = v);
			SetBool(data, Keys.Keyboard.EnableF10, v => settings.Keyboard.AllowFunctionKeys[10] = v);
			SetBool(data, Keys.Keyboard.EnableF11, v => settings.Keyboard.AllowFunctionKeys[11] = v);
			SetBool(data, Keys.Keyboard.EnableF12, v => settings.Keyboard.AllowFunctionKeys[12] = v);
		}

		private void MapMouse(IDictionary<string, object> data, AppSettings settings)
		{
			SetBool(data, Keys.Mouse.EnableMiddleMouseButton, v => settings.Mouse.AllowMiddleMouseButton = v);
			SetBool(data, Keys.Mouse.EnableRightMouseButton, v => settings.Mouse.AllowRightMouseButton = v);
		}

		private void MapSecurity(IDictionary<string, object> data, AppSettings settings)
		{
			SetBool(data, Keys.Security.AllowTermination, v => settings.Security.AllowTermination = v);
			SetBool(data, Keys.Security.AllowVirtualMachine, v => settings.Security.AllowVirtualMachine = v);
			SetBool(data, Keys.Security.AllowWindowCapture, v => settings.Security.AllowWindowCapture = v);
			SetBool(data, Keys.Security.AllowReconfiguration, v => settings.Security.AllowReconfiguration = v);
			SetBool(data, Keys.Security.DisableSessionChangeLockScreen, v => settings.Security.DisableSessionChangeLockScreen = v);
			SetBool(data, Keys.Security.VerifySessionIntegrity, v => settings.Security.VerifySessionIntegrity = v);
			SetString(data, Keys.Security.AdminPasswordHash, v => settings.Security.AdminPasswordHash = v);
			SetString(data, Keys.Security.QuitPasswordHash, v => settings.Security.QuitPasswordHash = v);
			SetString(data, Keys.Security.ReconfigurationUrl, v => settings.Security.ReconfigurationUrl = v);
			SetString(data, Keys.Security.VersionRestrictions, v => settings.Security.VersionRestrictions = v);

			if (data.TryGetValue(Keys.Security.ClipboardPolicy, out var policy) && policy is int clipboard)
			{
				settings.Security.ClipboardPolicy = clipboard switch
				{
					0 => ClipboardPolicy.Allow,
					1 => ClipboardPolicy.Block,
					_ => ClipboardPolicy.Isolated
				};
			}
		}

		private void MapServer(IDictionary<string, object> data, AppSettings settings)
		{
			SetString(data, Keys.Server.ApiEndpoint, v => settings.Server.ApiEndpoint = v);
			SetString(data, Keys.Server.ClientName, v => settings.Server.ClientName = v);
			SetString(data, Keys.Server.ClientSecret, v => settings.Server.ClientSecret = v);
			SetString(data, Keys.Server.ExamId, v => settings.Server.ExamId = v);
			SetString(data, Keys.Server.FallbackPasswordHash, v => settings.Server.FallbackPasswordHash = v);
			SetString(data, Keys.Server.Institution, v => settings.Server.Institution = v);
			SetString(data, Keys.Server.ServerUrl, v => settings.Server.ServerUrl = v);
			SetBool(data, Keys.Server.PerformFallback, v => settings.Server.PerformFallback = v);
			SetBool(data, Keys.Server.ShowRaiseHand, v => settings.Server.ShowRaiseHand = v);

			if (data.TryGetValue(Keys.Server.PingInterval, out var interval) && interval is int ping)
			{
				settings.Server.PingInterval = ping;
			}
		}

		private void MapUserInterface(IDictionary<string, object> data, AppSettings settings)
		{
			SetBool(data, Keys.UserInterface.UserInterfaceMode, v => settings.UserInterface.TouchOptimized = v);
			SetBool(data, Keys.UserInterface.Taskbar.EnableTaskbar, v => settings.UserInterface.ShowTaskbar = v);
		}

		private string AsString(object value)
		{
			return value switch
			{
				string text => text,
				int integer => integer.ToString(CultureInfo.InvariantCulture),
				long l => l.ToString(CultureInfo.InvariantCulture),
				double d => d.ToString(CultureInfo.InvariantCulture),
				bool b => b ? "true" : "false",
				_ => null
			};
		}

		private void SetBool(IDictionary<string, object> data, string key, Action<bool> setter)
		{
			if (data.TryGetValue(key, out var value) && value is bool boolean)
			{
				setter(boolean);
			}
		}

		private void SetString(IDictionary<string, object> data, string key, Action<string> setter)
		{
			if (data.TryGetValue(key, out var value))
			{
				setter(AsString(value));
			}
		}
	}
}
