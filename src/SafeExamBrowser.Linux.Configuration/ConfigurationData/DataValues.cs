/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Configuration.ConfigurationData
{
	internal class DataValues
	{
		internal AppSettings LoadDefaultSettings()
		{
			var settings = new AppSettings();

			settings.Browser.AdditionalWindow.AllowAddressBar = false;
			settings.Browser.AdditionalWindow.AllowNavigation = true;
			settings.Browser.AdditionalWindow.AllowReloading = true;
			settings.Browser.AdditionalWindow.ShowReloadWarning = false;
			settings.Browser.AdditionalWindow.ShowToolbar = false;
			settings.Browser.AdditionalWindow.UrlPolicy = UrlPolicy.Never;
			settings.Browser.AdditionalWindow.WindowHeight = "100";
			settings.Browser.AdditionalWindow.WindowPosition = "3";
			settings.Browser.AdditionalWindow.WindowWidth = "50";
			settings.Browser.AllowConfigurationDownloads = true;
			settings.Browser.AllowCustomDownUploadLocation = false;
			settings.Browser.AllowDownloads = true;
			settings.Browser.AllowFind = true;
			settings.Browser.AllowPageZoom = true;
			settings.Browser.AllowPdfReader = true;
			settings.Browser.AllowPdfReaderToolbar = false;
			settings.Browser.AllowPrint = false;
			settings.Browser.AllowSpellChecking = true;
			settings.Browser.AllowUploads = false;
			settings.Browser.DeleteCacheOnShutdown = true;
			settings.Browser.DeleteCookiesOnShutdown = true;
			settings.Browser.DeleteCookiesOnStartup = true;
			settings.Browser.EnableBrowser = true;
			settings.Browser.MainWindow.AllowAddressBar = false;
			settings.Browser.MainWindow.AllowNavigation = false;
			settings.Browser.MainWindow.AllowReloading = true;
			settings.Browser.MainWindow.ShowReloadButton = false;
			settings.Browser.MainWindow.ShowReloadWarning = true;
			settings.Browser.MainWindow.ShowToolbar = false;
			settings.Browser.MainWindow.UrlPolicy = UrlPolicy.Never;
			settings.Browser.MainWindow.WindowHeight = "100";
			settings.Browser.MainWindow.WindowMode = WindowMode.Windowed;
			settings.Browser.MainWindow.WindowPosition = "1";
			settings.Browser.MainWindow.WindowWidth = "100";
			settings.Browser.ResetOnQuitUrl = false;
			settings.Browser.SendBrowserExamKey = false;
			settings.Browser.SendConfigurationKey = false;
			settings.Browser.StartUrl = "https://www.safeexambrowser.org/start";
			settings.Browser.UseIsolatedClipboard = true;
			settings.Browser.UseQueryParameter = false;
			settings.Browser.UseTemporaryDownUploadDirectory = false;

			settings.ConfigurationFile.ConfigurationMode = ConfigurationMode.Exam;
			settings.ConfigurationFile.SessionMode = SessionMode.Normal;

			settings.General.LogLevel = LogLevel.Debug;

			settings.Keyboard.AllowAltEsc = false;
			settings.Keyboard.AllowAltF4 = false;
			settings.Keyboard.AllowAltTab = true;
			settings.Keyboard.AllowCtrlC = true;
			settings.Keyboard.AllowCtrlEsc = false;
			settings.Keyboard.AllowCtrlV = true;
			settings.Keyboard.AllowCtrlX = true;
			settings.Keyboard.AllowEsc = true;

			for (var i = 1; i <= 12; i++)
			{
				settings.Keyboard.AllowFunctionKeys[i] = true;
			}

			settings.Keyboard.AllowPrintScreen = false;
			settings.Keyboard.AllowSystemKey = false;

			settings.Mouse.AllowMiddleMouseButton = false;
			settings.Mouse.AllowRightMouseButton = true;

			settings.Security.AllowApplicationLog = false;
			settings.Security.AllowReconfiguration = false;
			settings.Security.AllowTermination = true;
			settings.Security.AllowVirtualMachine = false;
			settings.Security.AllowWindowCapture = false;
			settings.Security.ClipboardPolicy = ClipboardPolicy.Isolated;
			settings.Security.DisableSessionChangeLockScreen = false;
			settings.Security.VerifySessionIntegrity = true;

			settings.Server.PerformFallback = false;
			settings.Server.PingInterval = 1000;

			settings.UserInterface.ShowTaskbar = false;
			settings.UserInterface.TouchOptimized = false;

			return settings;
		}
	}
}
