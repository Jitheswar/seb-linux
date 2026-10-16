/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Client on Windows).
 */

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Browser;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;
using SafeExamBrowser.Linux.Security;

namespace SafeExamBrowser.Linux.Client
{
	/// <summary>
	/// The reason why an exam session has ended.
	/// </summary>
	public enum ExamSessionEndReason
	{
		/// <summary>
		/// The session was ended via the quit URL (or an external termination request).
		/// </summary>
		Terminated,

		/// <summary>
		/// The browser process exited without a termination request.
		/// </summary>
		BrowserExited
	}

	public sealed class ExamSessionOptions
	{
		/// <summary>
		/// The Chromium executable to be used, or null to auto-detect.
		/// </summary>
		public string ChromiumExecutablePath { get; set; }

		/// <summary>
		/// The directory for downloads, or null to use the configuration or the default directory.
		/// </summary>
		public string DownloadDirectory { get; set; }

		/// <summary>
		/// Determines whether the browser is to be launched in kiosk mode, or null to use the window mode of the configuration.
		/// </summary>
		public bool? KioskMode { get; set; }

		/// <summary>
		/// The logger to be used, or null to use a console logger.
		/// </summary>
		public ILogger Logger { get; set; }

		/// <summary>
		/// The user data directory to be used, or null for the default directory.
		/// </summary>
		public string ProfileDirectory { get; set; }

		/// <summary>
		/// Invoked when termination is requested but a quit password must be entered first.
		/// Returns the entered password, or null if the request has been aborted.
		/// </summary>
		public Func<string> QuitPasswordPrompt { get; set; }
	}

	/// <summary>
	/// Orchestrates an exam session: launches the browser with the loaded configuration,
	/// handles termination and reset requests and monitors the browser process.
	/// </summary>
	public sealed class ExamSession
	{
		private readonly ILogger logger;
		private readonly ExamSessionOptions options;
		private readonly AppSettings settings;

		public ExamSession(AppSettings settings, ExamSessionOptions options = null)
		{
			this.settings = settings;
			this.options = options ?? new ExamSessionOptions();
			logger = this.options.Logger ?? new ConsoleLogger("Client", debug: true);
		}

		/// <summary>
		/// Runs the exam session until it is terminated, i.e. until the quit URL has been
		/// visited (and the quit password, if configured, has been verified), the browser
		/// process has exited or the given cancellation token has been cancelled.
		/// </summary>
		public async Task<ExamSessionEndReason> RunAsync(CancellationToken cancellationToken = default)
		{
			var environmentProblem = ValidateEnvironment(settings);

			if (environmentProblem != default)
			{
				throw new InvalidOperationException(environmentProblem);
			}

			var attempt = 0;

			while (true)
			{
				attempt++;

				var browserExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				var resetRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				var terminationRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

				using (cancellationToken.Register(() => terminationRequested.TrySetResult(true)))
				{
					logger.Info($"Starting exam session (attempt {attempt})...");

					await using (var controller = CreateController(browserExited, resetRequested, terminationRequested))
					{
						await controller.LaunchAsync(cancellationToken);

						logger.Info($"Exam session is running, start URL: '{settings.Browser.StartUrl}'.");

						using (var monitor = CreateProcessMonitor())
						{
							monitor?.Start();

							var completed = await Task.WhenAny(terminationRequested.Task, resetRequested.Task, browserExited.Task);

							if (completed == resetRequested.Task)
							{
								logger.Info("The session is being reset, the browser will be restarted with a clean session.");
								continue;
							}

							if (completed == browserExited.Task)
							{
								logger.Warn("The browser process has exited unexpectedly!");

								return ExamSessionEndReason.BrowserExited;
							}

							logger.Info("Termination requested, shutting down the exam session...");

							return ExamSessionEndReason.Terminated;
						}
					}
				}
			}
		}

		/// <summary>
		/// Validates the environment in which the exam session is to be run: virtual machines
		/// are rejected unless the configuration allows them (see Security.AllowVirtualMachine).
		/// Returns null if the environment is valid, otherwise a description of the problem.
		/// </summary>
		public static string ValidateEnvironment(AppSettings settings)
		{
			if (settings != default && !settings.Security.AllowVirtualMachine && VirtualMachineDetector.IsVirtualMachine())
			{
				return "This exam is not allowed to be run inside a virtual machine!";
			}

			return default;
		}

		/// <summary>
		/// Validates that the given settings are sufficient to run an exam session. Returns
		/// null if the settings are valid, otherwise a description of the problem.
		/// </summary>
		public static string Validate(AppSettings settings)
		{
			if (settings == default)
			{
				return "No configuration has been loaded.";
			}

			if (settings.ConfigurationFile.SessionMode == SessionMode.Server)
			{
				return "This configuration requires a SEB Server session, which is not supported by this client yet!";
			}

			if (string.IsNullOrWhiteSpace(settings.Browser.StartUrl))
			{
				return "The configuration does not define a start URL for the exam!";
			}

			return default;
		}

		private BrowserController CreateController(TaskCompletionSource<bool> browserExited, TaskCompletionSource<bool> resetRequested, TaskCompletionSource<bool> terminationRequested)
		{
			var controller = new BrowserController(new BrowserConfiguration
			{
				Settings = settings.Browser,
				Security = settings.Security,
				KioskMode = options.KioskMode,
				ChromiumExecutablePath = options.ChromiumExecutablePath,
				DownloadDirectory = options.DownloadDirectory,
				ProfileDirectory = options.ProfileDirectory,
				Logger = logger
			});

			controller.ResetRequested += () =>
			{
				logger.Info("The quit URL has been visited and the session is to be reset.");

				resetRequested.TrySetResult(true);
			};

			controller.TerminationRequested += () =>
			{
				if (!VerifyQuitPassword())
				{
					return;
				}

				logger.Info("The quit URL has been visited and termination has been confirmed.");

				terminationRequested.TrySetResult(true);
			};

			controller.DownloadCompleted += (url, fileName, filePath) => logger.Info($"Download completed: '{fileName}' -> '{filePath}'.");
			controller.RequestBlocked += url => logger.Warn($"Blocked request for '{url}'.");

			_ = controller.WaitForExitAsync().ContinueWith(task => browserExited.TrySetResult(true), TaskContinuationOptions.OnlyOnRanToCompletion);

			return controller;
		}

		private ProcessMonitor CreateProcessMonitor()
		{
			var policy = new ProcessPolicy(settings.Applications);

			if (!policy.HasRules)
			{
				return default;
			}

			var monitor = new ProcessMonitor(policy, logger);

			// The browser and the client itself must never be terminated by the monitor.
			monitor.Ignore("chrome");
			monitor.Ignore("chromium");
			monitor.Ignore("chromium-browser");
			monitor.Ignore(Process.GetCurrentProcess().ProcessName);
			monitor.Ignore("seb-linux");

			monitor.ProcessDetected += process => logger.Warn($"Blocked application '{process.Name}' ({process.ProcessId}) is running!");

			logger.Info($"Process monitor activated with {settings.Applications.Whitelist.Count + settings.Applications.Blacklist.Count} configured application(s).");

			return monitor;
		}

		private bool VerifyQuitPassword()
		{
			if (!PasswordVerification.IsSet(settings.Security.QuitPasswordHash))
			{
				return true;
			}

			var password = options.QuitPasswordPrompt?.Invoke();

			if (PasswordVerification.Verify(password, settings.Security.QuitPasswordHash))
			{
				return true;
			}

			logger.Warn("Termination has been denied: the entered quit password is not correct.");

			return false;
		}
	}
}
