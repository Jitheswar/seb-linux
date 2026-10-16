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
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Server;

namespace SafeExamBrowser.Linux.Tests
{
	public class AppSignatureKeyTests
	{
		// Reference vector generated with the Java implementation of the SEB Server
		// (Cryptor.decryptASK: PBKDF2WithHmacSHA1 + AES/GCM/NoPadding, iv = first 16 bytes).
		private const string TOKEN = "test-connection-token";
		private const string SALT = "00112233445566778899aabbccddeeff";
		private const string SIGNATURE = "safe-exam-browser-linux";
		private const string IV = "000102030405060708090a0b0c0d0e0f";
		private const string REFERENCE = "000102030405060708090a0b0c0d0e0f5b9efe173a7669a25c0b5b4e59f002a18157d7ab2819d4a6a6601f293e5550d319679515858a18";

		[Fact]
		public void MustMatchReferenceVector()
		{
			var ask = AppSignatureKey.Compute(TOKEN, SALT, SIGNATURE, Convert.FromHexString(IV));

			Assert.Equal(REFERENCE, ask);
		}

		[Fact]
		public void MustBeDecryptableLikeTheSebServer()
		{
			var ask = AppSignatureKey.Compute(TOKEN, SALT, SIGNATURE);

			Assert.Equal(SIGNATURE, AppSignatureKey.Decrypt(ask, TOKEN, SALT));
		}

		[Fact]
		public void MustUseRandomIvByDefault()
		{
			var first = AppSignatureKey.Compute(TOKEN, SALT, SIGNATURE);
			var second = AppSignatureKey.Compute(TOKEN, SALT, SIGNATURE);

			Assert.NotEqual(first, second);
			Assert.Equal(SIGNATURE, AppSignatureKey.Decrypt(first, TOKEN, SALT));
			Assert.Equal(SIGNATURE, AppSignatureKey.Decrypt(second, TOKEN, SALT));
		}

		[Fact]
		public void MustRejectTamperedData()
		{
			var ask = AppSignatureKey.Compute(TOKEN, SALT, SIGNATURE);
			var data = Convert.FromHexString(ask);

			data[data.Length - 1] ^= 0xff;

			Assert.Throws<CryptographicException>(() => AppSignatureKey.Decrypt(Convert.ToHexString(data), TOKEN, SALT));
		}

		[Fact]
		public void MustRejectWrongToken()
		{
			var ask = AppSignatureKey.Compute(TOKEN, SALT, SIGNATURE);

			Assert.Throws<CryptographicException>(() => AppSignatureKey.Decrypt(ask, "other-token", SALT));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(1)]
		[InlineData(15)]
		[InlineData(16)]
		[InlineData(17)]
		[InlineData(100)]
		public void MustMatchAesGcmForStandardNonce(int length)
		{
			// Validates the GCM implementation against System.Security.Cryptography.AesGcm
			// (which only supports 12 byte nonces) for various plain text lengths.
			var key = AppSignatureKey.DeriveKey(TOKEN, Convert.FromHexString(SALT));
			var iv = new byte[12];
			var plainText = new byte[length];
			var cipherText = new byte[length];
			var tag = new byte[16];

			RandomNumberGenerator.Fill(iv);
			RandomNumberGenerator.Fill(plainText);

			Gcm.Encrypt(key, iv, plainText, cipherText, tag);

			var expectedCipherText = new byte[length];
			var expectedTag = new byte[16];

			using (var gcm = new AesGcm(key, 16))
			{
				gcm.Encrypt(iv, plainText, expectedCipherText, expectedTag);
			}

			Assert.Equal(expectedCipherText, cipherText);
			Assert.Equal(expectedTag, tag);

			var decrypted = new byte[length];

			Gcm.Decrypt(key, iv, cipherText, decrypted, tag);

			Assert.Equal(plainText, decrypted);
		}

		[Fact]
		public void MustRejectInvalidSalt()
		{
			Assert.Throws<ArgumentException>(() => AppSignatureKey.Compute(TOKEN, "not-hex", SIGNATURE));
			Assert.Throws<ArgumentException>(() => AppSignatureKey.Compute(TOKEN, default(string), SIGNATURE));
			Assert.Throws<ArgumentException>(() => AppSignatureKey.Compute(default(string), SALT, SIGNATURE));
		}
	}
}
