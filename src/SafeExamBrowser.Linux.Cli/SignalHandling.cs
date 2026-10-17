/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port.
 */

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SafeExamBrowser.Linux.Cli
{
	/// <summary>
	/// Installs termination-signal handlers (SIGINT, SIGTERM and SIGHUP on Linux,
	/// Ctrl+C on Windows) that invoke a managed callback. This mirrors what .NET's
	/// PosixSignalRegistration does, but is implemented directly here because the
	/// managed API is not available with all supported runtime installs. The native
	/// handler does exactly one thing — an interlocked integer store — and the actual
	/// callback is invoked by a low-frequency poller thread, so no managed state is
	/// touched in signal context. Without it, signalling the CLI (kill, SSH disconnect)
	/// would leave the browser process running, so every termination signal ends the
	/// exam session cleanly.
	/// </summary>
	internal static class SignalHandling
	{
		// POSIX signal numbers (identical on all Linux architectures).
		private const int SIGHUP = 1;
		private const int SIGINT = 2;
		private const int SIGTERM = 15;

		private const int POLL_INTERVAL_MILLISECONDS = 20;

		private static int pendingSignal;

		// Kept alive for the process lifetime: the function pointer is owned by libc.
		private static SignalCallback callback;

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void SignalCallback(int signal);

		// Layout of glibc's struct sigaction on 64-bit Linux: the handler pointer,
		// sigset_t (1024 bits = 16 x 64-bit words), sa_flags (int) and the restorer
		// pointer (152 bytes in total). The size must match exactly: sigaction()
		// writes the previous handler into the oldAction parameter, so an undersized
		// struct corrupts memory and crashes the process.
		[StructLayout(LayoutKind.Sequential)]
		private struct NativeSigAction
		{
			public IntPtr Handler;

			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
			public ulong[] Mask;

			public int Flags;
			public IntPtr Restorer;
		}

		[DllImport("libc", EntryPoint = "sigaction")]
		private static extern int SigAction(int signum, ref NativeSigAction action, ref NativeSigAction oldAction);

		/// <summary>
		/// Registers onSignal for SIGINT, SIGTERM and SIGHUP (Linux) or Ctrl+C (other
		/// platforms). The returned registration restores the default handlers on
		/// disposal, before the process exits.
		/// </summary>
		public static IDisposable Register(Action onSignal)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				return new ConsoleRegistration(onSignal);
			}

			// A closure-free static method: GetFunctionPointerForDelegate does not
			// support lambdas that capture state.
			callback = HandleSignal;

			Install(SIGHUP);
			Install(SIGINT);
			Install(SIGTERM);

			var poller = new Thread(() => Poll(onSignal))
			{
				IsBackground = true,
				Name = "SafeExamBrowser-Signals"
			};
			poller.Start();

			return new Registration(poller);
		}

		// Invoked from a C-level signal context: keep this free of any managed work.
		private static void HandleSignal(int signal)
		{
			Interlocked.Exchange(ref pendingSignal, signal);
		}

		private static void Poll(Action onSignal)
		{
			try
			{
				while (true)
				{
					Thread.Sleep(POLL_INTERVAL_MILLISECONDS);

					var signal = Interlocked.Exchange(ref pendingSignal, 0);

					if (signal == 0)
					{
						continue;
					}

					try
					{
						onSignal();
					}
					catch (Exception)
					{
						// The termination path must never be interrupted by a faulty
						// callback; the session state is re-evaluated on the next signal.
					}
				}
			}
			catch (ThreadInterruptedException)
			{
				// The registration has been disposed; an unhandled exception on this
				// thread would abort the process during shutdown.
			}
		}

		private static void Install(int signal)
		{
			var action = new NativeSigAction
			{
				Handler = Marshal.GetFunctionPointerForDelegate(callback),
				Mask = new ulong[16],
				Flags = 0,
				Restorer = IntPtr.Zero
			};
			var oldAction = new NativeSigAction { Mask = new ulong[16] };

			SigAction(signal, ref action, ref oldAction);
		}

		private static void Reset(int signal)
		{
			var action = new NativeSigAction
			{
				Handler = IntPtr.Zero, // SIG_DFL
				Mask = new ulong[16],
				Flags = 0,
				Restorer = IntPtr.Zero
			};
			var oldAction = new NativeSigAction { Mask = new ulong[16] };

			SigAction(signal, ref action, ref oldAction);
		}

		private sealed class Registration : IDisposable
		{
			private readonly Thread poller;

			public Registration(Thread poller)
			{
				this.poller = poller;
			}

			public void Dispose()
			{
				// Restore the default handlers before the process exits: a live managed
				// callback registered via sigaction makes the runtime abort during
				// shutdown on some platforms.
				Reset(SIGHUP);
				Reset(SIGINT);
				Reset(SIGTERM);

				callback = null;

				poller?.Interrupt();
			}
		}

		private sealed class ConsoleRegistration : IDisposable
		{
			private readonly Action onSignal;

			public ConsoleRegistration(Action onSignal)
			{
				this.onSignal = onSignal;

				Console.CancelKeyPress += OnCancelKeyPress;
			}

			private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
			{
				e.Cancel = true;

				onSignal();
			}

			public void Dispose()
			{
				Console.CancelKeyPress -= OnCancelKeyPress;
			}
		}
	}
}
