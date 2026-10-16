/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 *
 * Layouts written (all readable by SEB for Windows/macOS):
 *   plain, uncompressed : <plist XML>
 *   plain, compressed   : gzip(<plist XML>)
 *   plain data block    : gzip("plnd" + <plist XML>)
 *   password protected  : gzip("pswd" + AES-256(v2, gzip(<plist XML>)))
 */

using System.Collections.Generic;
using System.IO;
using System.Text;
using SafeExamBrowser.Linux.Configuration.Contracts;
using SafeExamBrowser.Linux.Configuration.Cryptography;
using SafeExamBrowser.Linux.Configuration.DataCompression;

namespace SafeExamBrowser.Linux.Configuration.DataFormats
{
	public class BinarySerializer
	{
		private readonly GZipCompressor compressor;
		private readonly ILogger logger;
		private readonly PasswordEncryption passwordEncryption;
		private readonly XmlSerializer xmlSerializer;

		public BinarySerializer(
			GZipCompressor compressor,
			ILogger logger,
			PasswordEncryption passwordEncryption,
			XmlSerializer xmlSerializer)
		{
			this.compressor = compressor;
			this.logger = logger;
			this.passwordEncryption = passwordEncryption;
			this.xmlSerializer = xmlSerializer;
		}

		public SerializeResult TrySerialize(IDictionary<string, object> data, PasswordParameters password = null, bool compressed = true, bool usePlainDataBlock = false)
		{
			var result = default(SerializeResult);

			if (password != null)
			{
				result = SerializePasswordBlock(data, password);
			}
			else if (usePlainDataBlock)
			{
				result = SerializePlainDataBlock(data, true, compressed);
			}
			else
			{
				result = SerializePlainXml(data, compressed);
			}

			return result;
		}

		private SerializeResult SerializePlainXml(IDictionary<string, object> data, bool compressed)
		{
			logger.Debug("Attempting to serialize plain XML data...");

			var result = xmlSerializer.TrySerialize(data);

			if (result.Status == SaveStatus.Success && compressed)
			{
				result.Data = compressor.Compress(result.Data);
			}

			return result;
		}

		private SerializeResult SerializePlainDataBlock(IDictionary<string, object> data, bool writePrefix = false, bool compressed = true)
		{
			logger.Debug("Attempting to serialize plain data block...");

			var result = xmlSerializer.TrySerialize(data);

			if (result.Status == SaveStatus.Success)
			{
				if (writePrefix)
				{
					result.Data = WritePrefix(BinaryBlock.PlainData, result.Data);
				}

				if (compressed)
				{
					result.Data = compressor.Compress(result.Data);
				}
			}

			return result;
		}

		private SerializeResult SerializePasswordBlock(IDictionary<string, object> data, PasswordParameters password)
		{
			// Inner payload is gzip-compressed before encryption, matching upstream layering.
			var result = SerializePlainDataBlock(data, false, true);

			if (result.Status == SaveStatus.Success)
			{
				var prefix = password.IsHash ? BinaryBlock.PasswordConfigureClient : BinaryBlock.Password;

				logger.Debug("Attempting to serialize password block...");

				var status = passwordEncryption.Encrypt(result.Data, password.Password, out var encrypted);

				if (status == SaveStatus.Success)
				{
					result.Data = WritePrefix(prefix, encrypted);
					result.Data = compressor.Compress(result.Data);
				}

				result.Status = status;
			}

			return result;
		}

		private Stream WritePrefix(string prefix, Stream data)
		{
			var prefixBytes = Encoding.UTF8.GetBytes(prefix);
			var stream = new MemoryStream();

			stream.Write(prefixBytes, 0, prefixBytes.Length);

			data.Seek(0, SeekOrigin.Begin);
			data.CopyTo(stream);
			stream.Seek(0, SeekOrigin.Begin);

			return stream;
		}
	}
}
