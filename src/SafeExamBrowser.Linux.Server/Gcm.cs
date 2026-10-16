/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port. Implements AES-GCM (NIST SP 800-38D) on top of
 * the AES block cipher and GHASH, as required for the app signature key exchange: the
 * SEB Server decrypts the app signature key with AES/GCM and a 16 byte IV
 * (see Cryptor.decryptASK), which System.Security.Cryptography.AesGcm does not support
 * (it only accepts 12 byte nonces).
 */

using System;
using System.Security.Cryptography;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// A minimal AES-GCM implementation (NIST SP 800-38D) which supports arbitrary IV lengths.
	/// </summary>
	internal static class Gcm
	{
		public const int BLOCK_SIZE = 16;

		/// <summary>
		/// Encrypts the plain text and writes the result to the cipher text and tag buffers.
		/// </summary>
		public static void Encrypt(byte[] key, byte[] iv, byte[] plainText, byte[] cipherText, byte[] tag)
		{
			Process(key, iv, plainText, cipherText, tag, encrypt: true);
		}

		/// <summary>
		/// Decrypts the cipher text and writes the result to the plain text buffer. Throws a
		/// <see cref="CryptographicException"/> if the given tag does not match.
		/// </summary>
		public static void Decrypt(byte[] key, byte[] iv, byte[] cipherText, byte[] plainText, byte[] tag)
		{
			Process(key, iv, cipherText, plainText, tag, encrypt: false);
		}

		private static void Process(byte[] key, byte[] iv, byte[] input, byte[] output, byte[] tag, bool encrypt)
		{
			using (var aes = Aes.Create())
			{
				aes.Key = key;
				aes.Mode = CipherMode.ECB;
				aes.Padding = PaddingMode.None;

				using (var encryptor = aes.CreateEncryptor())
				{
					var h = new byte[BLOCK_SIZE];

					EncryptBlock(encryptor, new byte[BLOCK_SIZE], h);

					var j0 = ComputeJ0(encryptor, h, iv);
					var counter = new byte[BLOCK_SIZE];
					var keyStream = new byte[BLOCK_SIZE];

					Buffer.BlockCopy(j0, 0, counter, 0, BLOCK_SIZE);

					for (var offset = 0; offset < input.Length; offset += BLOCK_SIZE)
					{
						IncrementCounter(counter);
						EncryptBlock(encryptor, counter, keyStream);

						var length = Math.Min(BLOCK_SIZE, input.Length - offset);

						for (var i = 0; i < length; i++)
						{
							output[offset + i] = (byte)(input[offset + i] ^ keyStream[i]);
						}
					}

					var hash = ComputeGhash(h, default(byte[]), encrypt ? output : input);
					var expectedTag = new byte[BLOCK_SIZE];
					var ej0 = new byte[BLOCK_SIZE];

					EncryptBlock(encryptor, j0, ej0);

					for (var i = 0; i < BLOCK_SIZE; i++)
					{
						expectedTag[i] = (byte)(hash[i] ^ ej0[i]);
					}

					if (encrypt)
					{
						Buffer.BlockCopy(expectedTag, 0, tag, 0, BLOCK_SIZE);
					}
					else if (!CryptographicOperations.FixedTimeEquals(expectedTag, tag))
					{
						throw new CryptographicException("The authentication tag does not match, the data has been modified or the key is incorrect!");
					}
				}
			}
		}

		private static byte[] ComputeJ0(ICryptoTransform encryptor, byte[] h, byte[] iv)
		{
			if (iv.Length == 12)
			{
				var j0 = new byte[BLOCK_SIZE];

				Buffer.BlockCopy(iv, 0, j0, 0, 12);
				j0[BLOCK_SIZE - 1] = 1;

				return j0;
			}

			// J0 = GHASH(IV || 0^s || 0^64 || [len(IV)]64)
			var padded = new byte[(iv.Length + BLOCK_SIZE - 1) / BLOCK_SIZE * BLOCK_SIZE + BLOCK_SIZE];

			Buffer.BlockCopy(iv, 0, padded, 0, iv.Length);
			WriteUInt64BE(padded, padded.Length - 8, (ulong)iv.Length * 8);

			var y = new byte[BLOCK_SIZE];

			Absorb(h, y, padded);

			return y;
		}

		private static byte[] ComputeGhash(byte[] h, byte[] additionalData, byte[] cipherText)
		{
			var y = new byte[BLOCK_SIZE];
			var lengthBlock = new byte[BLOCK_SIZE];

			Absorb(h, y, additionalData);
			Absorb(h, y, cipherText);

			WriteUInt64BE(lengthBlock, 0, (ulong)(additionalData?.Length ?? 0) * 8);
			WriteUInt64BE(lengthBlock, 8, (ulong)cipherText.Length * 8);

			XorInto(y, lengthBlock);
			Multiply(y, h, y);

			return y;
		}

		private static void Absorb(byte[] h, byte[] y, byte[] data)
		{
			if (data == default || data.Length == 0)
			{
				return;
			}

			var block = new byte[BLOCK_SIZE];

			for (var offset = 0; offset < data.Length; offset += BLOCK_SIZE)
			{
				Array.Clear(block, 0, BLOCK_SIZE);
				Buffer.BlockCopy(data, offset, block, 0, Math.Min(BLOCK_SIZE, data.Length - offset));

				XorInto(y, block);
				Multiply(y, h, y);
			}
		}

		/// <summary>
		/// Multiplies two elements of GF(2^128) as defined by GCM (bit-reflected convention),
		/// see NIST SP 800-38D, algorithm 1.
		/// </summary>
		private static void Multiply(byte[] x, byte[] y, byte[] result)
		{
			var z = new byte[BLOCK_SIZE];
			var v = (byte[])x.Clone();

			for (var i = 0; i < 128; i++)
			{
				if ((y[i / 8] & (0x80 >> (i % 8))) != 0)
				{
					XorInto(z, v);
				}

				var lsb = (v[BLOCK_SIZE - 1] & 1) != 0;

				RightShift(v);

				if (lsb)
				{
					v[0] ^= 0xe1;
				}
			}

			Buffer.BlockCopy(z, 0, result, 0, BLOCK_SIZE);
		}

		private static void RightShift(byte[] value)
		{
			for (var i = BLOCK_SIZE - 1; i > 0; i--)
			{
				value[i] = (byte)((value[i] >> 1) | ((value[i - 1] & 1) << 7));
			}

			value[0] >>= 1;
		}

		private static void IncrementCounter(byte[] counter)
		{
			for (var i = BLOCK_SIZE - 1; i >= BLOCK_SIZE - 4; i--)
			{
				if (++counter[i] != 0)
				{
					break;
				}
			}
		}

		private static void EncryptBlock(ICryptoTransform encryptor, byte[] input, byte[] output)
		{
			encryptor.TransformBlock(input, 0, BLOCK_SIZE, output, 0);
		}

		private static void XorInto(byte[] target, byte[] value)
		{
			for (var i = 0; i < BLOCK_SIZE; i++)
			{
				target[i] ^= value[i];
			}
		}

		private static void WriteUInt64BE(byte[] buffer, int offset, ulong value)
		{
			for (var i = 7; i >= 0; i--)
			{
				buffer[offset + i] = (byte)value;
				value >>= 8;
			}
		}
	}
}
