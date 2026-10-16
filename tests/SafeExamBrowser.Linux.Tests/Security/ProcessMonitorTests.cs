/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Diagnostics;
using System.IO;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Settings;
using SafeExamBrowser.Linux.Security;

namespace SafeExamBrowser.Linux.Tests
{
	public class ProcessMonitorTests
	{
		private readonly ILogger logger = new ConsoleLogger("tests", false);

		[Fact]
		public void MustFindBlockedProcessInScan()
		{
			using (var process = StartSleep())
			{
				var monitor = CreateMonitor("sleep");

				var blocked = WaitForScan(monitor, process.Id);

				Assert.NotNull(blocked);
				Assert.Equal("sleep", blocked.Name);
				Assert.Equal(process.Id, blocked.ProcessId);
			}
		}

		[Fact]
		public void MustIgnoreProcessesInScan()
		{
			using (var process = StartSleep())
			{
				var monitor = CreateMonitor("sleep");

				monitor.Ignore("SLEEP");

				Assert.Null(WaitForScan(monitor, process.Id, timeout: TimeSpan.FromSeconds(3)));
			}
		}

		[Fact]
		public void MustTerminateBlockedProcesses()
		{
			using (var process = StartSleep())
			{
				using (var monitor = CreateMonitor("sleep"))
				{
					monitor.Interval = TimeSpan.FromMilliseconds(200);
					monitor.Start();

					Assert.True(process.WaitForExit(10000), "The blocked process has not been terminated!");
				}
			}
		}

		private ProcessMonitor CreateMonitor(string blockedExecutable)
		{
			var applications = new ApplicationSettings();

			applications.Blacklist.Add(new ApplicationConfiguration { Active = true, AllowRunning = false, ExecutableName = blockedExecutable });

			return new ProcessMonitor(new ProcessPolicy(applications), logger);
		}

		private static Process StartSleep()
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = "sleep",
				UseShellExecute = false
			};

			startInfo.ArgumentList.Add("30");

			return Process.Start(startInfo);
		}

		private static BlockedProcess WaitForScan(ProcessMonitor monitor, int processId, TimeSpan timeout = default)
		{
			if (timeout == default)
			{
				timeout = TimeSpan.FromSeconds(5);
			}

			var end = DateTime.UtcNow.Add(timeout);

			while (DateTime.UtcNow < end)
			{
				foreach (var blocked in monitor.Scan())
				{
					if (blocked.ProcessId == processId)
					{
						return blocked;
					}
				}

				System.Threading.Thread.Sleep(100);
			}

			return default;
		}
	}
}
