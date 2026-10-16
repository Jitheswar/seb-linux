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
using System.Linq;
using SafeExamBrowser.Linux.Configuration.Settings;

namespace SafeExamBrowser.Linux.Security
{
	public enum ProcessPermission
	{
		Allow,
		Block
	}

	/// <summary>
	/// Evaluates whether an application is allowed to run according to the whitelist and
	/// blacklist of the configuration (see also SafeExamBrowser.Security on Windows).
	///
	/// The policy works as follows:
	///   - Active blacklist entries with AllowRunning=false block the application.
	///   - Active whitelist entries with AllowRunning=false block the application.
	///   - Active whitelist entries with AllowRunning=true allow the application, even if a
	///     blacklist entry matches.
	///   - Applications without any matching entry are allowed (the configured lists are
	///     used to forbid specific applications, not to restrict the whole system).
	///
	/// An entry matches if its executable name equals the process name (without path,
	/// case-insensitive) or, for entries with AllowCustomPath, if its executable path equals
	/// the full executable path of the process.
	/// </summary>
	public sealed class ProcessPolicy
	{
		private readonly IReadOnlyList<ApplicationConfiguration> blacklist;
		private readonly IReadOnlyList<ApplicationConfiguration> whitelist;

		public ProcessPolicy(ApplicationSettings applications)
		{
			blacklist = (applications?.Blacklist ?? new List<ApplicationConfiguration>()).Where(IsActive).ToList().AsReadOnly();
			whitelist = (applications?.Whitelist ?? new List<ApplicationConfiguration>()).Where(IsActive).ToList().AsReadOnly();
		}

		/// <summary>
		/// Determines whether the policy contains any active rules.
		/// </summary>
		public bool HasRules => blacklist.Count > 0 || whitelist.Count > 0;

		public ProcessPermission Evaluate(string executableName, string executablePath = default)
		{
			var denied = whitelist.Concat(blacklist).Any(entry => Matches(entry, executableName, executablePath) && !entry.AllowRunning);
			var allowed = whitelist.Any(entry => Matches(entry, executableName, executablePath) && entry.AllowRunning);

			if (allowed)
			{
				return ProcessPermission.Allow;
			}

			return denied ? ProcessPermission.Block : ProcessPermission.Allow;
		}

		private static bool IsActive(ApplicationConfiguration entry)
		{
			return entry.Active && !string.IsNullOrWhiteSpace(entry.ExecutableName);
		}

		private static bool Matches(ApplicationConfiguration entry, string executableName, string executablePath)
		{
			if (string.IsNullOrWhiteSpace(executableName))
			{
				return false;
			}

			var name = executableName.Trim();

			if (string.Equals(entry.ExecutableName.Trim(), name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return entry.AllowCustomPath
				&& !string.IsNullOrWhiteSpace(executablePath)
				&& !string.IsNullOrWhiteSpace(entry.ExecutablePath)
				&& string.Equals(entry.ExecutablePath.Trim(), executablePath.Trim(), StringComparison.Ordinal);
		}
	}
}
