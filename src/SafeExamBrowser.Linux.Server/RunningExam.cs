/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Server.Contracts.Data.Exam on Windows).
 */

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// An exam running on a SEB Server instance (see RunningExamInfo of the SEB Server Exam API v1).
	/// </summary>
	public sealed class RunningExam
	{
		/// <summary>
		/// The identifier of the exam.
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The type of the LMS used for the exam, e.g. "MOODLE".
		/// </summary>
		public string LmsType { get; set; }

		/// <summary>
		/// The name of the exam.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// The URL to which the client is redirected when the exam starts.
		/// </summary>
		public string Url { get; set; }

		public override string ToString()
		{
			return $"{Name} ({Id})";
		}
	}
}
