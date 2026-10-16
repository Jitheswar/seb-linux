/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Security on Windows).
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SafeExamBrowser.Linux.Configuration.Contracts;

namespace SafeExamBrowser.Linux.Security
{
	public sealed class BlockedProcess
	{
		public string ExecutablePath { get; set; }
		public string Name { get; set; }
		public int ProcessId { get; set; }
	}

	/// <summary>
	/// Monitors the running processes using the proc file system and reports (and optionally
	/// terminates) applications which are not allowed to run according to the configuration.
	/// Windows SEB monitors window messages and running applications, see
	/// SafeExamBrowser.Security.ProcessMonitor; on Linux the proc file system is used instead.
	/// </summary>
	public sealed class ProcessMonitor : IDisposable
	{
		private readonly List<string> ignoredNames = new List<string>();
		private readonly ILogger logger;
		private readonly ProcessPolicy policy;
		private readonly string procRoot;

		private CancellationTokenSource cancellation;
		private Task monitorTask;

		public ProcessMonitor(ProcessPolicy policy, ILogger logger, string procRoot = "/proc")
		{
			this.policy = policy ?? new ProcessPolicy(default);
			this.logger = logger;
			this.procRoot = procRoot;
		}

		/// <summary>
		/// Invoked when a blocked process has been detected and termination is disabled.
		/// </summary>
		public event Action<BlockedProcess> ProcessDetected;

		/// <summary>
		/// The interval in which the running processes are checked.
		/// </summary>
		public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

		/// <summary>
		/// Determines whether blocked processes are to be terminated.
		/// </summary>
		public bool TerminateBlockedProcesses { get; set; } = true;

		/// <summary>
		/// Adds a process name which is never to be reported or terminated (e.g. the browser).
		/// </summary>
		public void Ignore(string executableName)
		{
			if (!string.IsNullOrWhiteSpace(executableName))
			{
				ignoredNames.Add(executableName.Trim());
			}
		}

		public void Start()
		{
			cancellation = new CancellationTokenSource();
			monitorTask = Task.Run(() => MonitorLoopAsync(cancellation.Token));
		}

		public void Stop()
		{
			cancellation?.Cancel();

			try
			{
				monitorTask?.Wait(TimeSpan.FromSeconds(5));
			}
			catch (AggregateException)
			{
			}

			cancellation?.Dispose();
			cancellation = default;
		}

		/// <summary>
		/// Scans all currently running processes once and returns the blocked ones.
		/// </summary>
		public IReadOnlyList<BlockedProcess> Scan()
		{
			var blocked = new List<BlockedProcess>();

			try
			{
				foreach (var processDirectory in Directory.EnumerateDirectories(procRoot))
				{
					var name = Path.GetFileName(processDirectory);

					if (!int.TryParse(name, out var processId))
					{
						continue;
					}

					var process = Inspect(processId, processDirectory);

					if (process != default && policy.Evaluate(process.Name, process.ExecutablePath) == ProcessPermission.Block && !IsIgnored(process.Name))
					{
						blocked.Add(process);
					}
				}
			}
			catch (Exception e)
			{
				logger.Error("Could not scan the running processes!", e);
			}

			return blocked;
		}

		public void Dispose()
		{
			Stop();
		}

		private bool IsIgnored(string executableName)
		{
			return ignoredNames.Exists(name => string.Equals(name, executableName, StringComparison.OrdinalIgnoreCase));
		}

		private BlockedProcess Inspect(int processId, string processDirectory)
		{
			try
			{
				var namePath = Path.Combine(processDirectory, "comm");
				var executablePath = default(string);

				if (!File.Exists(namePath))
				{
					return default;
				}

				var name = File.ReadAllText(namePath).Trim();

				if (string.IsNullOrWhiteSpace(name))
				{
					return default;
				}

				try
				{
					executablePath = new FileInfo(Path.Combine(processDirectory, "exe")).LinkTarget;
				}
				catch (Exception)
				{
					// The executable path is not accessible for processes of other users.
				}

				return new BlockedProcess { ExecutablePath = executablePath, Name = name, ProcessId = processId };
			}
			catch (Exception)
			{
				// The process has exited, ignore it.
				return default;
			}
		}

		private async Task MonitorLoopAsync(CancellationToken cancellationToken)
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(Interval, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				foreach (var process in Scan())
				{
					logger.Warn($"Detected blocked process '{process.Name}' ({process.ProcessId}).");

					if (TerminateBlockedProcesses && Terminate(process))
					{
						logger.Info($"Terminated blocked process '{process.Name}' ({process.ProcessId}).");
					}
					else
					{
						ProcessDetected?.Invoke(process);
					}
				}
			}
		}

		private bool Terminate(BlockedProcess process)
		{
			try
			{
				using (var target = Process.GetProcessById(process.ProcessId))
				{
					target.Kill();

					return true;
				}
			}
			catch (Exception e)
			{
				logger.Warn($"Could not terminate process '{process.Name}' ({process.ProcessId}): {e.Message}");

				return false;
			}
		}
	}
}
