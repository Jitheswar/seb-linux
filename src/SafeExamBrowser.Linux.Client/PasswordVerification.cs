/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port.
 */

using System;
using System.Security.Cryptography;
using System.Text;

namespace SafeExamBrowser.Linux.Client
{
	/// <summary>
	/// Verifies quit and administration passwords against the hashes stored in a SEB
	/// configuration (SHA256, hexadecimal, see also SafeExamBrowser.Security on Windows).
	/// </summary>
	public static class PasswordVerification
	{
		/// <summary>
		/// Determines whether a password hash has been configured.
		/// </summary>
		public static bool IsSet(string hash)
		{
			return !string.IsNullOrWhiteSpace(hash);
		}

		/// <summary>
		/// Verifies the given password against the given hash. An empty hash accepts any
		/// (including no) password.
		/// </summary>
		public static bool Verify(string password, string hash)
		{
			if (!IsSet(hash))
			{
				return true;
			}

			if (string.IsNullOrEmpty(password))
			{
				return false;
			}

			var computed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

			return string.Equals(computed, hash.Trim(), StringComparison.OrdinalIgnoreCase);
		}
	}
}
