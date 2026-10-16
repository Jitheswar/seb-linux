/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using SafeExamBrowser.Linux.Configuration.Settings;
using SafeExamBrowser.Linux.Security;

namespace SafeExamBrowser.Linux.Tests
{
	public class ProcessPolicyTests
	{
		[Fact]
		public void MustAllowEverythingWithoutRules()
		{
			var policy = new ProcessPolicy(new ApplicationSettings());

			Assert.False(policy.HasRules);
			Assert.Equal(ProcessPermission.Allow, policy.Evaluate("firefox"));
		}

		[Fact]
		public void MustBlockActiveBlacklistEntry()
		{
			var policy = new ProcessPolicy(Applications(blacklist: Entry("firefox", active: true, allowRunning: false)));

			Assert.True(policy.HasRules);
			Assert.Equal(ProcessPermission.Block, policy.Evaluate("firefox"));
		}

		[Fact]
		public void MustBlockActiveWhitelistEntryNotAllowedToRun()
		{
			var policy = new ProcessPolicy(Applications(whitelist: Entry("firefox", active: true, allowRunning: false)));

			Assert.Equal(ProcessPermission.Block, policy.Evaluate("firefox"));
		}

		[Fact]
		public void MustPreferWhitelistExceptionOverBlacklist()
		{
			var policy = new ProcessPolicy(Applications(
				whitelist: Entry("firefox", active: true, allowRunning: true),
				blacklist: Entry("firefox", active: true, allowRunning: false)));

			Assert.Equal(ProcessPermission.Allow, policy.Evaluate("firefox"));
		}

		[Fact]
		public void MustMatchExecutableNameIgnoringCase()
		{
			var policy = new ProcessPolicy(Applications(blacklist: Entry("FireFox", active: true, allowRunning: false)));

			Assert.Equal(ProcessPermission.Block, policy.Evaluate("firefox"));
		}

		[Fact]
		public void MustIgnoreInactiveEntries()
		{
			var policy = new ProcessPolicy(Applications(blacklist: Entry("firefox", active: false, allowRunning: false)));

			Assert.False(policy.HasRules);
			Assert.Equal(ProcessPermission.Allow, policy.Evaluate("firefox"));
		}

		[Fact]
		public void MustIgnoreEntriesWithoutExecutableName()
		{
			var policy = new ProcessPolicy(Applications(blacklist: Entry("  ", active: true, allowRunning: false)));

			Assert.False(policy.HasRules);
		}

		[Fact]
		public void MustMatchCustomPathOnlyWhenConfigured()
		{
			var entry = Entry("firefox", active: true, allowRunning: false);

			entry.AllowCustomPath = true;
			entry.ExecutablePath = "/usr/lib/firefox/firefox";

			var policy = new ProcessPolicy(Applications(blacklist: entry));

			Assert.Equal(ProcessPermission.Block, policy.Evaluate("other", "/usr/lib/firefox/firefox"));
			Assert.Equal(ProcessPermission.Allow, policy.Evaluate("other", "/usr/bin/firefox"));
		}

		[Fact]
		public void MustAllowUnrelatedProcesses()
		{
			var policy = new ProcessPolicy(Applications(blacklist: Entry("firefox", active: true, allowRunning: false)));

			Assert.Equal(ProcessPermission.Allow, policy.Evaluate("gedit"));
		}

		private static ApplicationSettings Applications(ApplicationConfiguration blacklist = null, ApplicationConfiguration whitelist = null)
		{
			var applications = new ApplicationSettings();

			if (blacklist != null)
			{
				applications.Blacklist.Add(blacklist);
			}

			if (whitelist != null)
			{
				applications.Whitelist.Add(whitelist);
			}

			return applications;
		}

		private static ApplicationConfiguration Entry(string executableName, bool active, bool allowRunning)
		{
			return new ApplicationConfiguration { Active = active, AllowRunning = allowRunning, ExecutableName = executableName };
		}
	}
}
