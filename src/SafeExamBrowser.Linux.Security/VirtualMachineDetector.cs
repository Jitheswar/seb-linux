/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Security.VirtualMachineDetector on Windows).
 */

using System;
using System.IO;

namespace SafeExamBrowser.Linux.Security
{
	/// <summary>
	/// Detects whether SEB is running inside a virtual machine by inspecting the DMI
	/// information of the system (see /sys/class/dmi/id). Windows SEB does the same via
	/// WMI and the system firmware tables, see SafeExamBrowser.Security.VirtualMachineDetector.
	/// </summary>
	public static class VirtualMachineDetector
	{
		private static readonly string[] indicators =
		{
			"bochs",
			"bhyve",
			"hyper-v",
			"innotek",
			"kvm",
			"parallels",
			"qemu",
			"virtual machine",
			"virtualbox",
			"vmware",
			"xen"
		};

		private static readonly string[] sources =
		{
			"class/dmi/id/product_name",
			"class/dmi/id/sys_vendor",
			"class/dmi/id/board_vendor",
			"class/dmi/id/bios_vendor"
		};

		/// <summary>
		/// Determines whether the system is running inside a virtual machine.
		/// </summary>
		public static bool IsVirtualMachine(string sysRoot = "/sys")
		{
			foreach (var source in sources)
			{
				if (Matches(source, sysRoot))
				{
					return true;
				}
			}

			return false;
		}

		internal static bool Matches(string source, string sysRoot)
		{
			try
			{
				var path = Path.Combine(sysRoot, source);

				if (!File.Exists(path))
				{
					return false;
				}

				var value = File.ReadAllText(path).Trim();

				foreach (var indicator in indicators)
				{
					if (value.Contains(indicator, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
			catch (Exception)
			{
				// Treat unreadable sources as inconclusive.
			}

			return false;
		}
	}
}
