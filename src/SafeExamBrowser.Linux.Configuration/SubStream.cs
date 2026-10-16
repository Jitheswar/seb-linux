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

namespace SafeExamBrowser.Linux.Configuration
{
	/// <summary>
	/// A read-only, seekable view over a subsection of another stream.
	/// Port of SafeExamBrowser.Configuration.SubStream.
	/// </summary>
	internal class SubStream : Stream
	{
		private readonly long length;
		private readonly long offset;
		private readonly Stream original;

		private long position;

		public override bool CanRead => original.CanRead;
		public override bool CanSeek => original.CanSeek;
		public override bool CanWrite => false;
		public override long Length => length;

		public override long Position
		{
			get => position;
			set => position = value;
		}

		public SubStream(Stream original, long offset, long length)
		{
			this.original = original;
			this.offset = offset;
			this.length = length;

			if (!original.CanRead)
			{
				throw new ArgumentException("The original stream must support reading!", nameof(original));
			}

			if (!original.CanSeek)
			{
				throw new ArgumentException("The original stream must support seeking!", nameof(original));
			}

			if (original.Length < offset + length || offset < 0 || length < 1)
			{
				throw new ArgumentOutOfRangeException($"Specified subsection is outside the bounds of the original stream!");
			}
		}

		public override void Flush()
		{
			throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			var originalPosition = original.Position;
			var dataRead = default(int);

			original.Seek(this.offset + Position, SeekOrigin.Begin);
			dataRead = original.Read(buffer, offset, (int) Math.Min(count, Length - Position));
			original.Seek(originalPosition, SeekOrigin.Begin);

			Position += dataRead;

			return dataRead;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			switch (origin)
			{
				case SeekOrigin.Begin:
					Position = offset;
					break;
				case SeekOrigin.Current:
					Position += offset;
					break;
				case SeekOrigin.End:
					Position = Length + offset;
					break;
			}

			return Position;
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}
}
