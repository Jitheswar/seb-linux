/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System.Security.Cryptography;
using System.Text;
using System;
using System.Globalization;

namespace SafeExamBrowser.Linux.Configuration.Cryptography
{
	/// <summary>
	/// Generates password hashes as used in SEB configuration files (hashedQuitPassword, hashedAdminPassword).
	/// </summary>
	public class HashAlgorithm
	{
		public string GenerateHashFor(string password)
		{
			var bytes = Encoding.UTF8.GetBytes(password);
			var hash = SHA256.HashData(bytes);
			var hashString = Convert.ToHexString(hash).ToLowerInvariant();

			return hashString;
		}
	}
}
