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
using System.Collections.Generic;
using System.IO;

namespace SafeExamBrowser.Linux.Configuration.Contracts
{
	public enum LoadStatus
	{
		InvalidData,
		NotSupported,
		PasswordNeeded,
		Success,
		UnexpectedError
	}

	public enum SaveStatus
	{
		InvalidData,
		NotSupported,
		Success,
		UnexpectedError
	}

	public enum FormatType
	{
		Binary,
		Xml
	}

	public class EncryptionParameters
	{
	}

	public class PasswordParameters : EncryptionParameters
	{
		public string Password { get; set; }
		public bool IsHash { get; set; }
	}

	public class PublicKeyParameters : EncryptionParameters
	{
		public byte[] Certificate { get; set; }
		public PasswordParameters InnerEncryption { get; set; }
		public bool SymmetricEncryption { get; set; }
	}

	public class ParseResult
	{
		public EncryptionParameters Encryption { get; set; }
		public FormatType Format { get; set; }
		public IDictionary<string, object> RawData { get; set; }
		public LoadStatus Status { get; set; }
	}

	public class SerializeResult
	{
		public Stream Data { get; set; }
		public SaveStatus Status { get; set; }
	}

	public interface ILogger
	{
		void Debug(string message);
		void Error(string message, Exception exception = null);
		void Info(string message);
		void Warn(string message);
	}

	/// <summary>
	/// Minimal console logger, lineage: SafeExamBrowser.Logging.
	/// </summary>
	public class ConsoleLogger : ILogger
	{
		private readonly object @lock = new object();
		private readonly string category;

		public bool IsDebugEnabled { get; set; }

		public ConsoleLogger(string category = "SEB", bool debug = false)
		{
			this.category = category;
			this.IsDebugEnabled = debug;
		}

		public void Debug(string message)
		{
			if (IsDebugEnabled)
			{
				Write("DEBUG", message);
			}
		}

		public void Error(string message, Exception exception = null)
		{
			Write("ERROR", exception == null ? message : $"{message} {exception}");
		}

		public void Info(string message)
		{
			Write("INFO ", message);
		}

		public void Warn(string message)
		{
			Write("WARN ", message);
		}

		private void Write(string level, string message)
		{
			lock (@lock)
			{
				Console.Error.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {level} | {category} | {message}");
			}
		}
	}
}
