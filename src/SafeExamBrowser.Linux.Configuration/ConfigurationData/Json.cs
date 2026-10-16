/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 *
 * This is the canonical "SEB-JSON" serialization which defines the Config Key.
 * See https://safeexambrowser.org/developer/seb-config-key.html for the specification.
 *
 * IMPORTANT: every byte written here is part of the Config Key contract with
 * exam systems (e.g. the Moodle quizaccess_seb plugin). Do not change the
 * serialization format without checking compatibility.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SafeExamBrowser.Linux.Configuration.ConfigurationData
{
	internal static class Json
	{
		internal static void Serialize(IDictionary<string, object> dictionary, StreamWriter stream)
		{
			var orderedByKey = dictionary.OrderBy(d => d.Key, StringComparer.InvariantCulture).ToList();

			stream.Write('{');

			foreach (var kvp in orderedByKey)
			{
				var process = true;

				process &= !kvp.Key.Equals(Keys.General.OriginatorVersion, StringComparison.OrdinalIgnoreCase);
				process &= !(kvp.Value is IDictionary<string, object> d) || d.Any();

				if (process)
				{
					stream.Write('"');
					stream.Write(kvp.Key);
					stream.Write('"');
					stream.Write(':');

					Serialize(kvp.Value, stream);

					if (kvp.Key != orderedByKey.Last().Key)
					{
						stream.Write(',');
					}
				}
			}

			stream.Write('}');
		}

		private static void Serialize(IList<object> list, StreamWriter stream)
		{
			stream.Write('[');

			foreach (var item in list)
			{
				Serialize(item, stream);

				if (item != list.Last())
				{
					stream.Write(',');
				}
			}

			stream.Write(']');
		}

		private static void Serialize(object value, StreamWriter stream)
		{
			switch (value)
			{
				case IDictionary<string, object> dictionary:
					Serialize(dictionary, stream);
					break;
				case IList<object> list:
					Serialize(list, stream);
					break;
				case byte[] data:
					stream.Write('"');
					stream.Write(Convert.ToBase64String(data));
					stream.Write('"');
					break;
				case DateTime date:
					stream.Write(date.ToString("o"));
					break;
				case bool boolean:
					stream.Write(boolean.ToString().ToLower());
					break;
				case int integer:
					stream.Write(integer.ToString(NumberFormatInfo.InvariantInfo));
					break;
				case double number:
					stream.Write(number.ToString(NumberFormatInfo.InvariantInfo));
					break;
				case string text:
					stream.Write('"');
					stream.Write(text);
					stream.Write('"');
					break;
				case null:
					stream.Write('"');
					stream.Write('"');
					break;
			}
		}
	}
}
