/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.IO;
using SafeExamBrowser.Linux.Security;

namespace SafeExamBrowser.Linux.Tests
{
	public class VirtualMachineDetectorTests
	{
		[Theory]
		[InlineData("product_name", "VMware Virtual Platform")]
		[InlineData("product_name", "VirtualBox")]
		[InlineData("product_name", "Standard PC (QEMU)")]
		[InlineData("product_name", "KVM")]
		[InlineData("product_name", "Virtual Machine")]
		[InlineData("sys_vendor", "QEMU")]
		[InlineData("sys_vendor", "innotek GmbH")]
		[InlineData("product_name", "Microsoft Corporation Virtual Machine")]
		[InlineData("bios_vendor", "Xen")]
		[InlineData("board_vendor", "Parallels Software International Inc.")]
		public void MustDetectVirtualMachines(string source, string value)
		{
			using (var system = new TestSystem())
			{
				system.Write(source, value);

				Assert.True(VirtualMachineDetector.IsVirtualMachine(system.Root));
			}
		}

		[Theory]
		[InlineData("product_name", "XPS 15 9520")]
		[InlineData("sys_vendor", "Dell Inc.")]
		[InlineData("board_vendor", "ASUSTeK COMPUTER INC.")]
		public void MustNotDetectPhysicalMachines(string source, string value)
		{
			using (var system = new TestSystem())
			{
				system.Write(source, value);

				Assert.False(VirtualMachineDetector.IsVirtualMachine(system.Root));
			}
		}

		[Fact]
		public void MustNotDetectAnythingWithoutDmiInformation()
		{
			using (var system = new TestSystem())
			{
				Assert.False(VirtualMachineDetector.IsVirtualMachine(system.Root));
			}
		}

		private sealed class TestSystem : IDisposable
		{
			public TestSystem()
			{
				Root = Path.Combine(Path.GetTempPath(), $"seb-sys-{Guid.NewGuid():N}");

				Directory.CreateDirectory(Path.Combine(Root, "class", "dmi", "id"));
			}

			public string Root { get; }

			public void Write(string source, string value)
			{
				File.WriteAllText(Path.Combine(Root, "class", "dmi", "id", source), value);
			}

			public void Dispose()
			{
				Directory.Delete(Root, recursive: true);
			}
		}
	}
}
