/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Server on Windows).
 */

using System;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// Thrown when communication with a SEB Server instance fails.
	/// </summary>
	public sealed class SebServerException : Exception
	{
		public SebServerException(string message, Exception innerException = null) : base(message, innerException)
		{
		}
	}
}
