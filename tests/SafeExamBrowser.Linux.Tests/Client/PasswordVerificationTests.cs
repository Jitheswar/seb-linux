/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Security.Cryptography;
using System.Text;
using SafeExamBrowser.Linux.Client;

namespace SafeExamBrowser.Linux.Tests
{
	public class PasswordVerificationTests
	{
		private static readonly string Secret = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("secret"))).ToLowerInvariant();

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("   ")]
		public void MustAcceptAnyPasswordWhenNoHashConfigured(string hash)
		{
			Assert.False(PasswordVerification.IsSet(hash));
			Assert.True(PasswordVerification.Verify(null, hash));
			Assert.True(PasswordVerification.Verify("anything", hash));
		}

		[Fact]
		public void MustVerifyCorrectPassword()
		{
			Assert.True(PasswordVerification.IsSet(Secret));
			Assert.True(PasswordVerification.Verify("secret", Secret));
		}

		[Fact]
		public void MustVerifyIgnoringCaseAndWhitespace()
		{
			Assert.True(PasswordVerification.Verify("secret", Secret.ToUpperInvariant()));
			Assert.True(PasswordVerification.Verify("secret", $"  {Secret}  "));
		}

		[Theory]
		[InlineData("wrong")]
		[InlineData("Secret")]
		[InlineData("")]
		public void MustRejectIncorrectPassword(string password)
		{
			Assert.False(PasswordVerification.Verify(password, Secret));
		}

		[Fact]
		public void MustRejectMissingPasswordWhenHashConfigured()
		{
			Assert.False(PasswordVerification.Verify(null, Secret));
		}
	}
}
