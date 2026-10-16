/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Integrity.IntegrityModule
 * and Cryptor.decryptASK of the SEB Server for the reference algorithm).
 */

using System;
using System.Security.Cryptography;
using System.Text;
using SafeExamBrowser.Linux.Configuration.Contracts;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// Calculates the app signature key ("ASK") which is sent to a SEB Server instance during
	/// the handshake (parameter "seb_signature_key"). The key proves the identity of the SEB
	/// build to the server: the server decrypts it with the connection token and the exam salt
	/// and stores its hash, which administrators can grant for their exams (see
	/// SecurityKeyServiceImpl.getAppSignatureKeyHash of the SEB Server).
	///
	/// Algorithm (as implemented by Cryptor.decryptASK of the SEB Server):
	///   key        = PBKDF2-HMAC-SHA1(password = connection token, salt, 1024 iterations, 32 bytes)
	///   ciphertext = AES-256-GCM(key, iv = 16 random bytes, plaintext = app signature)
	///   result     = hex(iv + ciphertext + tag)
	/// </summary>
	public static class AppSignatureKey
	{
		public const int ITERATIONS = 1024;
		public const int KEY_SIZE_BYTES = 32;
		public const int IV_SIZE_BYTES = 16;
		public const int TAG_SIZE_BYTES = 16;

		/// <summary>
		/// Calculates the app signature key for the given connection token and exam salt (hex).
		/// The plaintext is the build identifier of this client (see <see cref="AppInfo.CodeSignatureHash"/>).
		/// </summary>
		public static string Compute(string connectionToken, string saltHex, string appSignature = null, byte[] iv = null)
		{
			if (string.IsNullOrEmpty(connectionToken))
			{
				throw new ArgumentException("A connection token is required to calculate the app signature key!", nameof(connectionToken));
			}

			var salt = ParseSalt(saltHex);
			var signature = appSignature ?? AppInfo.CodeSignatureHash;
			var key = DeriveKey(connectionToken, salt);

			if (iv == default)
			{
				iv = RandomNumberGenerator.GetBytes(IV_SIZE_BYTES);
			}

			var plainText = Encoding.UTF8.GetBytes(signature);
			var cipherText = new byte[plainText.Length];
			var tag = new byte[TAG_SIZE_BYTES];

			Gcm.Encrypt(key, iv, plainText, cipherText, tag);

			var result = new byte[iv.Length + cipherText.Length + tag.Length];

			Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
			Buffer.BlockCopy(cipherText, 0, result, iv.Length, cipherText.Length);
			Buffer.BlockCopy(tag, 0, result, iv.Length + cipherText.Length, tag.Length);

			return ToHex(result);
		}

		/// <summary>
		/// Decrypts an app signature key as done by a SEB Server instance. Used for verification
		/// and testing.
		/// </summary>
		public static string Decrypt(string cipherTextHex, string connectionToken, string saltHex)
		{
			var salt = ParseSalt(saltHex);
			var key = DeriveKey(connectionToken, salt);
			var data = FromHex(cipherTextHex);

			if (data.Length < IV_SIZE_BYTES + TAG_SIZE_BYTES)
			{
				throw new ArgumentException("The cipher text is too short to contain an IV and a tag!", nameof(cipherTextHex));
			}

			var iv = new byte[IV_SIZE_BYTES];
			var cipherText = new byte[data.Length - IV_SIZE_BYTES - TAG_SIZE_BYTES];
			var tag = new byte[TAG_SIZE_BYTES];

			Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
			Buffer.BlockCopy(data, IV_SIZE_BYTES, cipherText, 0, cipherText.Length);
			Buffer.BlockCopy(data, data.Length - TAG_SIZE_BYTES, tag, 0, tag.Length);

			var plainText = new byte[cipherText.Length];

			Gcm.Decrypt(key, iv, cipherText, plainText, tag);

			return Encoding.UTF8.GetString(plainText);
		}

		public static byte[] DeriveKey(string connectionToken, byte[] salt)
		{
			return Rfc2898DeriveBytes.Pbkdf2(connectionToken, salt, ITERATIONS, HashAlgorithmName.SHA1, KEY_SIZE_BYTES);
		}

		private static byte[] ParseSalt(string saltHex)
		{
			if (string.IsNullOrEmpty(saltHex))
			{
				throw new ArgumentException("An app signature key salt is required!", nameof(saltHex));
			}

			try
			{
				return FromHex(saltHex);
			}
			catch (FormatException e)
			{
				throw new ArgumentException($"The app signature key salt '{saltHex}' is not a valid hexadecimal string!", nameof(saltHex), e);
			}
		}

		private static byte[] FromHex(string hex)
		{
			return Convert.FromHexString(hex.Trim());
		}

		private static string ToHex(byte[] bytes)
		{
			return Convert.ToHexString(bytes).ToLowerInvariant();
		}
	}
}
