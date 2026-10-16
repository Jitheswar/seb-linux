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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Browser;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;
using SafeExamBrowser.Linux.Server;

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

		/// <summary>
		/// The password for encrypted configurations, used when a new configuration is
		/// downloaded during the session, or null if the configuration is not encrypted.
		/// </summary>
		public string ConfigurationPassword { get; set; }
	}

	/// <summary>
	/// Orchestrates an exam session: launches the browser with the loaded configuration,
	/// handles termination and reset requests and monitors the browser process.
	///
	/// If the configuration requires a SEB Server session (see
	/// https://github.com/SafeExamBrowser/seb-server), the session first connects to the
	/// server, downloads the exam configuration and then runs the exam while pinging the
	/// server for instructions (e.g. termination).
	/// </summary>
	public sealed class ExamSession
	{
		private readonly ILogger logger;
		private readonly ExamSessionOptions options;

		private volatile TaskCompletionSource<bool> currentTermination;
		private AppSettings settings;

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
			if (settings.ConfigurationFile.SessionMode == SessionMode.Server)
			{
				return await RunServerSessionAsync(cancellationToken);
			}

			return await RunBrowserSessionAsync(cancellationToken);
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
				var connection = SebServerConnection.FromSettings(settings.Server);

				return connection == default
					? "The configuration does not define a SEB Server URL (sebServerURL)!"
					: connection.Validate();
			}

			if (string.IsNullOrWhiteSpace(settings.Browser.StartUrl))
			{
				return "The configuration does not define a start URL for the exam!";
			}

			return default;
		}

		/// <summary>
		/// Runs a SEB Server session: connects to the server, selects the exam, downloads the
		/// exam configuration, then runs the exam browser while pinging the server for
		/// instructions until the session ends. The connection is terminated afterwards.
		/// </summary>
		private async Task<ExamSessionEndReason> RunServerSessionAsync(CancellationToken cancellationToken)
		{
			var connection = SebServerConnection.FromSettings(settings.Server);

			var problem = connection == default
				? "The configuration does not define a SEB Server URL (sebServerURL)!"
				: connection.Validate();

			if (problem != default)
			{
				throw new InvalidOperationException(problem);
			}

			using (var client = new SebServerClient(connection, logger))
			{
				try
				{
					await client.AuthenticateAsync(cancellationToken);

					var exam = await SelectExamAsync(client, cancellationToken);
					var (salt, browserExamKey) = await client.SelectExamAsync(exam.Id, cancellationToken);
					var appSignatureKey = default(string);

					if (!string.IsNullOrWhiteSpace(salt))
					{
						appSignatureKey = AppSignatureKey.Compute(client.ConnectionToken, salt, AppInfo.CodeSignatureHash);
					}

					await client.EstablishConnectionAsync(appSignatureKey, cancellationToken);

					var data = await client.GetExamConfigurationAsync(exam.Id, cancellationToken);
					var result = new ConfigurationLoader(logger).LoadData(data, $"seb-server:{exam.Id}");

					if (result.Status != LoadStatus.Success)
					{
						throw new InvalidOperationException($"Failed to load the exam configuration provided by the SEB Server ({result.Status})!");
					}

					settings = result.Settings;

					if (!string.IsNullOrWhiteSpace(browserExamKey))
					{
						logger.Info("Using the browser exam key provided by the SEB Server for all requests.");
						settings.Browser.CustomBrowserExamKey = browserExamKey;
					}

					if (string.IsNullOrWhiteSpace(settings.Browser.StartUrl))
					{
						throw new InvalidOperationException("The exam configuration provided by the SEB Server does not define a start URL!");
					}

					logger.Info($"Exam '{exam.Name}' is ready, start URL: '{settings.Browser.StartUrl}'.");

					return await RunWithPingsAsync(client, cancellationToken);
				}
				finally
				{
					await CloseConnectionAsync(client);
				}
			}
		}

		/// <summary>
		/// Runs the browser session while pinging the SEB Server for instructions in the
		/// background.
		/// </summary>
		private async Task<ExamSessionEndReason> RunWithPingsAsync(SebServerClient client, CancellationToken cancellationToken)
		{
			using (var pings = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				var task = RunPingLoopAsync(client, pings.Token);

				try
				{
					return await RunBrowserSessionAsync(cancellationToken);
				}
				finally
				{
					pings.Cancel();

					try
					{
						await task;
					}
					catch (OperationCanceledException)
					{
						// The session has ended.
					}
					catch (Exception e)
					{
						logger.Warn($"The SEB Server ping loop has ended unexpectedly: {e.Message}");
					}
				}
			}
		}

		private async Task RunPingLoopAsync(SebServerClient client, CancellationToken cancellationToken)
		{
			var confirmations = new ConcurrentQueue<string>();
			var interval = settings.Server.PingInterval > 0 ? settings.Server.PingInterval : 1000;
			var pingNumber = 0;

			logger.Info($"Started pinging the SEB Server (interval: {interval} ms).");

			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(interval, cancellationToken);

				var confirmation = default(string);

				confirmations.TryDequeue(out confirmation);

				SebServerInstruction instruction;

				try
				{
					instruction = await client.PingAsync(++pingNumber, confirmation, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception e)
				{
					logger.Warn($"Failed to send ping to the SEB Server: {e.Message}");
					continue;
				}

				if (instruction == default)
				{
					continue;
				}

				if (!string.IsNullOrWhiteSpace(instruction.InstructionConfirmation))
				{
					confirmations.Enqueue(instruction.InstructionConfirmation);
				}

				HandleInstruction(instruction);
			}

			logger.Info("Stopped pinging the SEB Server.");
		}

		private void HandleInstruction(SebServerInstruction instruction)
		{
			switch (instruction.Instruction)
			{
				case SebServerInstructionType.Quit:
					logger.Info("The SEB Server requested to terminate the session.");
					currentTermination?.TrySetResult(true);
					break;

				case SebServerInstructionType.NotificationConfirm:
					logger.Info($"The SEB Server confirmed a notification ({instruction.Type}, #{instruction.Id}).");
					break;

				case SebServerInstructionType.LockScreen:
					logger.Warn("The SEB Server requested to lock the screen, which is not supported by this client yet!");
					break;

				case SebServerInstructionType.Proctoring:
				case SebServerInstructionType.ProctoringReconfiguration:
					logger.Warn($"The SEB Server requested '{instruction.Instruction}', which is not supported by this client yet!");
					break;

				default:
					logger.Warn($"Received an unknown instruction ('{instruction.Instruction}') from the SEB Server.");
					break;
			}
		}

		private async Task<RunningExam> SelectExamAsync(SebServerClient client, CancellationToken cancellationToken)
		{
			var examId = settings.Server.ExamId;
			var exams = await client.HandshakeAsync(examId, cancellationToken);

			if (!string.IsNullOrWhiteSpace(examId))
			{
				var exam = exams.FirstOrDefault(e => string.Equals(e.Id, examId, StringComparison.Ordinal));

				if (exam == default)
				{
					throw new InvalidOperationException($"The SEB Server does not provide the exam '{examId}' (running exams: {Describe(exams)})!");
				}

				return exam;
			}

			if (exams.Count == 0)
			{
				throw new InvalidOperationException("The SEB Server does not provide any running exam!");
			}

			if (exams.Count > 1)
			{
				throw new InvalidOperationException($"The SEB Server provides multiple running exams, the exam to be run must be specified via the configuration (exam ID). Running exams: {Describe(exams)}");
			}

			logger.Info($"Selected the only running exam: {exams[0]}");

			return exams[0];
		}

		private async Task CloseConnectionAsync(SebServerClient client)
		{
			if (client.ConnectionToken == default)
			{
				return;
			}

			try
			{
				await client.CloseAsync(CancellationToken.None);
			}
			catch (Exception e)
			{
				logger.Warn($"Failed to terminate the connection with the SEB Server: {e.Message}");
			}
		}

		private static string Describe(IReadOnlyList<RunningExam> exams)
		{
			return exams.Count == 0 ? "none" : string.Join(", ", exams.Select(exam => exam.ToString()));
		}

		private async Task<ExamSessionEndReason> RunBrowserSessionAsync(CancellationToken cancellationToken)
		{
			var attempt = 0;

			while (true)
			{
				attempt++;

				var browserExited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				var resetRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
				var terminationRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

				currentTermination = terminationRequested;

				using (cancellationToken.Register(() => terminationRequested.TrySetResult(true)))
				{
					logger.Info($"Starting exam session (attempt {attempt})...");

					await using (var controller = CreateController(browserExited, resetRequested, terminationRequested))
					{
						await controller.LaunchAsync(cancellationToken);

						logger.Info($"Exam session is running, start URL: '{settings.Browser.StartUrl}'.");

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

		private BrowserController CreateController(TaskCompletionSource<bool> browserExited, TaskCompletionSource<bool> resetRequested, TaskCompletionSource<bool> terminationRequested)
		{
			var controller = new BrowserController(new BrowserConfiguration
			{
				Settings = settings.Browser,
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

			controller.ConfigurationDownloadRequested += url => _ = HandleConfigurationDownloadAsync(url, resetRequested);

			_ = controller.WaitForExitAsync().ContinueWith(task => browserExited.TrySetResult(true), TaskContinuationOptions.OnlyOnRanToCompletion);

			return controller;
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

		/// <summary>
		/// Downloads the SEB configuration the user navigated to (allowed by
		/// allowConfigurationDownloads), validates it, applies it and resets the session
		/// so the browser restarts with the new configuration.
		/// </summary>
		private async Task HandleConfigurationDownloadAsync(string url, TaskCompletionSource<bool> resetRequested)
		{
			logger.Info($"Configuration download requested for '{url}'.");

			try
			{
				var result = await Task.Run(() => new ConfigurationLoader(logger).Load(url, options.ConfigurationPassword));

				if (result.Status != LoadStatus.Success)
				{
					logger.Warn($"Failed to load the requested configuration from '{url}' -> {result.Status}.");
					return;
				}

				var problem = ExamSession.Validate(result.Settings);

				if (problem != default)
				{
					logger.Warn($"The requested configuration from '{url}' is invalid: {problem}.");
					return;
				}

				settings = result.Settings;

				logger.Info("The new configuration has been applied, the session is being reset.");

				resetRequested.TrySetResult(true);
			}
			catch (Exception e)
			{
				logger.Warn($"Failed to download the configuration from '{url}': {e.Message}");
			}
		}
	}
}
