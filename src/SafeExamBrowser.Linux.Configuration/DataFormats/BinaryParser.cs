/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 */

using System;
using System.IO;
using System.Linq;
using System.Text;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Cryptography;
using SafeExamBrowser.Linux.Configuration.DataCompression;

namespace SafeExamBrowser.Linux.Configuration.DataFormats
{
	/// <summary>
	/// Parser for SEB's binary container format (legacy .seb files and current
	/// password-protected files). Block layout: [4-byte ASCII prefix][payload],
	/// optionally gzip-compressed as a whole.
	/// </summary>
	public class BinaryParser
	{
		private const int PREFIX_LENGTH = 4;

		private readonly GZipCompressor compressor;
		private readonly HashAlgorithm hashAlgorithm;
		private readonly ILogger logger;
		private readonly PasswordEncryption passwordEncryption;
		private readonly XmlParser xmlParser;

		public BinaryParser(
			GZipCompressor compressor,
			HashAlgorithm hashAlgorithm,
			ILogger logger,
			PasswordEncryption passwordEncryption,
			XmlParser xmlParser)
		{
			this.compressor = compressor;
			this.hashAlgorithm = hashAlgorithm;
			this.logger = logger;
			this.passwordEncryption = passwordEncryption;
			this.xmlParser = xmlParser;
		}

		public bool CanParse(Stream data)
		{
			try
			{
				var longEnough = data.Length > PREFIX_LENGTH;

				if (longEnough)
				{
					var prefix = ReadPrefix(data);
					var isValid = IsValid(prefix);

					logger.Debug($"'{data}' starting with '{prefix}' {(isValid ? "matches" : "does not match")} the {FormatType.Binary} format.");

					return isValid;
				}

				logger.Debug($"'{data}' is not long enough ({data.Length} bytes) to match the {FormatType.Binary} format.");
			}
			catch (Exception e)
			{
				logger.Error($"Failed to determine whether '{data}' matches the {FormatType.Binary} format!", e);
			}

			return false;
		}

		public ParseResult TryParse(Stream data, PasswordParameters password = null)
		{
			var prefix = ReadPrefix(data);
			var isValid = IsValid(prefix);
			var result = new ParseResult { Status = LoadStatus.InvalidData };

			if (isValid)
			{
				data = compressor.IsCompressed(data) ? compressor.Decompress(data) : data;
				data = new SubStream(data, PREFIX_LENGTH, data.Length - PREFIX_LENGTH);

				switch (prefix)
				{
					case BinaryBlock.Password:
					case BinaryBlock.PasswordConfigureClient:
						result = ParsePasswordBlock(data, prefix, password);
						break;
					case BinaryBlock.PlainData:
						result = ParsePlainDataBlock(data);
						break;
					case BinaryBlock.PublicKey:
					case BinaryBlock.PublicKeySymmetric:
						result = ParsePublicKeyBlock(data, prefix);
						break;
				}

				result.Format = FormatType.Binary;
			}
			else
			{
				logger.Error($"'{data}' starting with '{prefix}' does not match the {FormatType.Binary} format!");
			}

			return result;
		}

		private ParseResult ParsePasswordBlock(Stream data, string prefix, PasswordParameters password = null)
		{
			var result = new ParseResult();

			if (password != null)
			{
				var parameters = DetermineEncryptionParametersFor(prefix, password);

				logger.Debug($"Attempting to parse password block with prefix '{prefix}'...");
				result.Status = passwordEncryption.Decrypt(data, parameters.Password, out var decrypted);

				if (result.Status == LoadStatus.Success)
				{
					result = ParsePlainDataBlock(decrypted);
					result.Encryption = parameters;
				}
			}
			else
			{
				result.Status = LoadStatus.PasswordNeeded;
			}

			return result;
		}

		private ParseResult ParsePlainDataBlock(Stream data)
		{
			data = compressor.IsCompressed(data) ? compressor.Decompress(data) : data;
			logger.Debug("Attempting to parse plain data block...");

			return xmlParser.TryParse(data);
		}

		/// <summary>
		/// Certificate-encrypted configuration files are not yet supported by the Linux port.
		/// They require CMS/PKCS#7 envelope decryption with the recipient's private key.
		/// </summary>
		private ParseResult ParsePublicKeyBlock(Stream data, string prefix)
		{
			logger.Error($"The configuration file is encrypted with a certificate (block '{prefix}'), which is not yet supported by SEB for Linux! " +
				$"Please ask for a configuration protected by a password or an unencrypted one.");

			return new ParseResult { Status = LoadStatus.NotSupported };
		}

		private PasswordParameters DetermineEncryptionParametersFor(string prefix, PasswordParameters password)
		{
			var parameters = new PasswordParameters();

			if (prefix == BinaryBlock.PasswordConfigureClient)
			{
				parameters.Password = password.IsHash ? password.Password : hashAlgorithm.GenerateHashFor(password.Password);
				parameters.IsHash = true;
			}
			else
			{
				parameters.Password = password.Password;
				parameters.IsHash = password.IsHash;
			}

			return parameters;
		}

		private string ReadPrefix(Stream data)
		{
			var prefix = new byte[PREFIX_LENGTH];

			if (compressor.IsCompressed(data))
			{
				prefix = compressor.Peek(data, PREFIX_LENGTH);
			}
			else
			{
				data.Seek(0, SeekOrigin.Begin);
				data.Read(prefix, 0, PREFIX_LENGTH);
			}

			return Encoding.UTF8.GetString(prefix);
		}

		private bool IsValid(string prefix)
		{
			return prefix == BinaryBlock.Password
				|| prefix == BinaryBlock.PasswordConfigureClient
				|| prefix == BinaryBlock.PlainData
				|| prefix == BinaryBlock.PublicKey
				|| prefix == BinaryBlock.PublicKeySymmetric;
		}
	}
}
