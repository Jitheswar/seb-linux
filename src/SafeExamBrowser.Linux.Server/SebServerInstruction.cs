/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * New implementation for the Linux port (see SafeExamBrowser.Server.Data on Windows).
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace SafeExamBrowser.Linux.Server
{
	/// <summary>
	/// The instruction types of the SEB Server Exam API, see also
	/// https://github.com/SafeExamBrowser/seb-server.
	/// </summary>
	public static class SebServerInstructionType
	{
		public const string LockScreen = "SEB_FORCE_LOCK_SCREEN";
		public const string NotificationConfirm = "NOTIFICATION_CONFIRM";
		public const string Proctoring = "SEB_PROCTORING";
		public const string ProctoringReconfiguration = "SEB_RECONFIGURE_SETTINGS";
		public const string Quit = "SEB_QUIT";
	}

	/// <summary>
	/// An instruction sent from a SEB Server instance to this client, received as the response
	/// to a ping (see https://github.com/SafeExamBrowser/seb-server). The server serializes
	/// instructions as {"instruction":"...","attributes":{...}}.
	/// </summary>
	public sealed class SebServerInstruction
	{
		public const string ATTRIBUTE_INSTRUCTION_CONFIRM = "instruction-confirm";

		private static readonly IReadOnlyDictionary<string, string> NO_ATTRIBUTES = new Dictionary<string, string>();

		/// <summary>
		/// The type of the instruction, see <see cref="SebServerInstructionType"/>.
		/// </summary>
		public string Instruction { get; set; }

		/// <summary>
		/// The attributes of the instruction. Values which are not JSON strings are converted
		/// to their raw JSON representation.
		/// </summary>
		public IReadOnlyDictionary<string, string> Attributes { get; set; } = NO_ATTRIBUTES;

		/// <summary>
		/// The value to be confirmed with the next ping, if the instruction requires a confirmation.
		/// </summary>
		public string InstructionConfirmation => GetAttribute(ATTRIBUTE_INSTRUCTION_CONFIRM);

		/// <summary>
		/// The identifier of a notification, if available.
		/// </summary>
		public int Id
		{
			get
			{
				var id = GetAttribute("id");

				return id != default && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
			}
		}

		/// <summary>
		/// The message of a notification, if available.
		/// </summary>
		public string Message => GetAttribute("message");

		/// <summary>
		/// The notification type, e.g. "lockscreen" or "raisehand", if available.
		/// </summary>
		public string Type => GetAttribute("type");

		public string GetAttribute(string name)
		{
			return Attributes != default && Attributes.TryGetValue(name, out var value) ? value : default;
		}

		/// <summary>
		/// Tries to parse an instruction from the given JSON document. Returns false if the
		/// content is not a valid instruction.
		/// </summary>
		public static bool TryParse(string json, out SebServerInstruction instruction)
		{
			instruction = default;

			if (string.IsNullOrWhiteSpace(json))
			{
				return false;
			}

			try
			{
				using (var document = JsonDocument.Parse(json))
				{
					var root = document.RootElement;

					if (root.ValueKind != JsonValueKind.Object)
					{
						return false;
					}

					var result = new SebServerInstruction { Attributes = NO_ATTRIBUTES };

					foreach (var property in root.EnumerateObject())
					{
						if (property.NameEquals("instruction"))
						{
							result.Instruction = property.Value.GetString();
						}
						else if (property.NameEquals("attributes") && property.Value.ValueKind == JsonValueKind.Object)
						{
							var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

							foreach (var attribute in property.Value.EnumerateObject())
							{
								attributes[attribute.Name] = ToString(attribute.Value);
							}

							result.Attributes = attributes;
						}
					}

					instruction = result.Instruction != default ? result : default;
				}
			}
			catch (JsonException)
			{
				instruction = default;
			}

			return instruction != default;
		}

		private static string ToString(JsonElement element)
		{
			return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
		}
	}
}
