/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System.Reflection;

namespace SafeExamBrowser.Linux.Configuration.Contracts
{
	/// <summary>
	/// Identification of this SEB build. These values influence the Browser Exam Key (BEK):
	///
	///   BEK = HMAC-SHA256(key = examKeySalt, message = CodeSignatureHash + ProgramBuildVersion + ConfigKey)
	///
	/// On Windows, SEB uses the Authenticode signature hash of the executable and its file
	/// version. On Linux there is no Authenticode signature, so a stable platform identifier is
	/// used instead. As with upstream SEB, the BEK changes when the SEB build version changes;
	/// exam administrators must then update the key in their LMS.
	/// </summary>
	public static class AppInfo
	{
		/// <summary>
		/// Stable platform signature identifier, analogous to the code signing certificate hash on Windows.
		/// Override with the environment variable SEB_LINUX_BEK_SIGNATURE for institutions which want a
		/// pinned, build-independent value.
		/// </summary>
		public static string CodeSignatureHash =>
			System.Environment.GetEnvironmentVariable("SEB_LINUX_BEK_SIGNATURE") ?? "safe-exam-browser-linux";

		/// <summary>
		/// The SEB build version, analogous to the file version on Windows. Part of the BEK.
		/// Build metadata (e.g. the source revision) is not part of the version.
		/// </summary>
		public static string ProgramBuildVersion
		{
			get
			{
				var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
					?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
					?? "0.0.0";

				var separator = version.IndexOf('+');

				return separator < 0 ? version : version.Substring(0, separator);
			}
		}
	}
}
