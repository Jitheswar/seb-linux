/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Minimal reader for the .NET BinaryFormatter wire format ("MS-NRBF"), used by the
 * test suite to decode the upstream binary test fixtures (TestDictionary*.bin).
 *
 * This reader is intentionally not part of the product code: SEB configuration files
 * are Apple plist XML (possibly gzip-compressed and/or password-encrypted), never
 * BinaryFormatter streams. It only needs to reconstruct the generic
 * Dictionary<string, object> object graphs used by the upstream unit tests.
 *
 * The wire format is defined by the .NET BinaryFormatter implementation; the relevant
 * details are documented inline. PrimitiveTypeEnum values are those of the internal
 * InternalPrimitiveTypeE enum (e.g. Int32 = 8), and primitive class member values are
 * written inline, while all other member values are full records.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SafeExamBrowser.Linux.Tests.Nrbf
{
	internal static class NrbfReader
	{
		/// <summary>
		/// Deserializes a BinaryFormatter stream containing a root Dictionary&lt;string, object&gt;.
		/// </summary>
		internal static IDictionary<string, object> DeserializeDictionary(Stream stream)
		{
			return new Parser(stream).DeserializeRootDictionary();
		}

		private sealed class Parser
		{
			private static readonly object MessageEnd = new object();

			private readonly BinaryReader reader;
			private readonly Dictionary<int, ClassMetadata> classes = new Dictionary<int, ClassMetadata>();
			private readonly HashSet<int> materializationChain = new HashSet<int>();
			private readonly Dictionary<int, object> objects = new Dictionary<int, object>();

			internal Parser(Stream stream)
			{
				reader = new BinaryReader(stream, Encoding.UTF8);
			}

			internal IDictionary<string, object> DeserializeRootDictionary()
			{
				var header = ReadRecord() as HeaderInfo ?? throw new InvalidDataException("The stream does not start with a valid BinaryFormatter header!");

				while (true)
				{
					if (ReadRecord() == MessageEnd)
					{
						break;
					}
				}

				var root = Materialize(objects[header.RootId]);

				return root as IDictionary<string, object> ?? throw new InvalidDataException("The root object is not a dictionary!");
			}

			private object ReadRecord()
			{
				var recordType = reader.ReadByte();

				switch (recordType)
				{
					case 0:
						return ReadHeader();
					case 1:
						return ReadClassWithId();
					case 2:
					case 3:
					case 4:
					case 5:
						return ReadClass(recordType);
					case 6:
						return ReadObjectString();
					case 7:
						return ReadBinaryArray();
					case 8:
						return ReadPrimitiveTyped();
					case 9:
						return new MemberReference(reader.ReadInt32());
					case 10:
						return null;
					case 11:
						return MessageEnd;
					case 12:
						return ReadLibrary();
					case 13:
						return new NullRun(reader.ReadByte());
					case 14:
						return new NullRun(reader.ReadInt32());
					case 15:
						return ReadSingleArray(true);
					case 16:
					case 17:
						return ReadSingleArray(false);
					default:
						throw new InvalidDataException($"Unsupported record type '{recordType}' at position {reader.BaseStream.Position - 1}!");
				}
			}

			private HeaderInfo ReadHeader()
			{
				var rootId = reader.ReadInt32();
				var headerId = reader.ReadInt32();
				var majorVersion = reader.ReadInt32();
				var minorVersion = reader.ReadInt32();

				if (majorVersion != 1 || minorVersion != 0)
				{
					throw new InvalidDataException($"Unsupported BinaryFormatter version {majorVersion}.{minorVersion}!");
				}

				return new HeaderInfo(rootId, headerId);
			}

			private object ReadClassWithId()
			{
				var objectId = reader.ReadInt32();
				var metadataId = reader.ReadInt32();

				if (!classes.TryGetValue(metadataId, out var metadata))
				{
					throw new InvalidDataException($"No class metadata with object ID {metadataId} has been read yet!");
				}

				classes[objectId] = metadata;

				return ReadClassRecordBody(objectId, metadata);
			}

			private object ReadClass(int recordType)
			{
				var objectId = reader.ReadInt32();
				var name = ReadString();
				var memberCount = reader.ReadInt32();
				var memberNames = new string[memberCount];

				for (var i = 0; i < memberCount; i++)
				{
					memberNames[i] = ReadString();
				}

				var metadata = new ClassMetadata(name, memberNames);

				if (recordType == 2 || recordType == 3)
				{
					// SystemClassWithMembers / ClassWithMembers: all members have BinaryType Object.
					metadata.BinaryTypes = new BinaryType[memberCount];
					metadata.PrimitiveTypes = new PrimitiveType[memberCount];

					for (var i = 0; i < memberCount; i++)
					{
						metadata.BinaryTypes[i] = BinaryType.Object;
					}

					if (recordType == 3)
					{
						reader.ReadInt32();
					}
				}
				else
				{
					ReadMemberTypeInfo(metadata);
				}

				classes[objectId] = metadata;

				return ReadClassRecordBody(objectId, metadata);
			}

			private void ReadMemberTypeInfo(ClassMetadata metadata)
			{
				var memberCount = metadata.MemberNames.Length;

				metadata.BinaryTypes = new BinaryType[memberCount];
				metadata.PrimitiveTypes = new PrimitiveType[memberCount];
				metadata.TypeNames = new string[memberCount];

				for (var i = 0; i < memberCount; i++)
				{
					metadata.BinaryTypes[i] = (BinaryType)reader.ReadByte();
				}

				for (var i = 0; i < memberCount; i++)
				{
					switch (metadata.BinaryTypes[i])
					{
						case BinaryType.Primitive:
						case BinaryType.PrimitiveArray:
							metadata.PrimitiveTypes[i] = (PrimitiveType)reader.ReadByte();
							break;
						case BinaryType.SystemClass:
						case BinaryType.Class:
							metadata.TypeNames[i] = ReadString();
							break;
					}
				}
			}

			private object ReadClassRecordBody(int objectId, ClassMetadata metadata)
			{
				var values = new object[metadata.MemberNames.Length];

				for (var i = 0; i < values.Length; i++)
				{
					// Primitive member values are written inline, all other values are full records.
					values[i] = metadata.BinaryTypes[i] == BinaryType.Primitive ? ReadPrimitiveValue(metadata.PrimitiveTypes[i]) : ReadRecord();
				}

				var instance = new ClassInstance(metadata, values);

				objects[objectId] = instance;

				return instance;
			}

			private object ReadObjectString()
			{
				var objectId = reader.ReadInt32();
				var value = ReadString();

				objects[objectId] = value;

				return value;
			}

			private object ReadLibrary()
			{
				reader.ReadInt32();
				ReadString();

				return null;
			}

			private object ReadPrimitiveTyped()
			{
				return ReadPrimitiveValue((PrimitiveType)reader.ReadByte());
			}

			private object ReadSingleArray(bool primitive)
			{
				var objectId = reader.ReadInt32();
				var length = reader.ReadInt32();
				var value = new ArrayValue { ElementType = primitive ? BinaryType.Primitive : BinaryType.Object };

				if (primitive)
				{
					value.PrimitiveType = (PrimitiveType)reader.ReadByte();
				}

				value.Values = ReadElements(value.ElementType, value.PrimitiveType, length);

				objects[objectId] = value;

				return value;
			}

			private object ReadBinaryArray()
			{
				var objectId = reader.ReadInt32();
				var arrayType = reader.ReadByte();
				var rank = reader.ReadInt32();
				var lengths = new int[rank];

				for (var i = 0; i < rank; i++)
				{
					lengths[i] = reader.ReadInt32();
				}

				if (arrayType >= 3)
				{
					// Offset arrays (SingleOffset, JaggedOffset, RectangularOffset) carry additional offsets.
					for (var i = 0; i < rank; i++)
					{
						reader.ReadInt32();
					}
				}

				var elementType = (BinaryType)reader.ReadByte();
				var value = new ArrayValue { ElementType = elementType };

				switch (elementType)
				{
					case BinaryType.Primitive:
						value.PrimitiveType = (PrimitiveType)reader.ReadByte();
						break;
					case BinaryType.SystemClass:
					case BinaryType.Class:
						value.TypeName = ReadString();
						break;
				}

				// Order of elements: [0] = Single/SingleOffset, [1] = Jagged/JaggedOffset, [2] = Rectangular/RectangularOffset.
				var dimension = arrayType >= 3 ? arrayType - 3 : arrayType;

				if (dimension == 0)
				{
					value.Values = ReadElements(elementType, value.PrimitiveType, lengths[0]);
				}
				else
				{
					var count = 1;

					foreach (var length in lengths)
					{
						count *= length;
					}

					if (dimension == 1)
					{
						// Jagged arrays: each element is a record (a nested array).
						value.Values = ReadElements(BinaryType.Object, null, lengths[0]);
					}
					else
					{
						value.Values = ReadElements(elementType, value.PrimitiveType, count);
					}
				}

				objects[objectId] = value;

				return value;
			}

			private List<object> ReadElements(BinaryType elementType, PrimitiveType? primitiveType, int count)
			{
				var values = new List<object>(count);

				while (values.Count < count)
				{
					if (elementType == BinaryType.Primitive)
					{
						values.Add(ReadPrimitiveValue(primitiveType.Value));
					}
					else
					{
						var element = ReadRecord();

						if (element is NullRun run)
						{
							for (var i = 0; i < run.Count && values.Count < count; i++)
							{
								values.Add(null);
							}
						}
						else
						{
							values.Add(element);
						}
					}
				}

				return values;
			}

			private object ReadPrimitiveValue(PrimitiveType type)
			{
				switch (type)
				{
					case PrimitiveType.Boolean:
						return reader.ReadBoolean();
					case PrimitiveType.Byte:
						return reader.ReadByte();
					case PrimitiveType.Char:
						return (char)reader.ReadUInt16();
					case PrimitiveType.Currency:
					case PrimitiveType.Decimal:
						return reader.ReadDecimal();
					case PrimitiveType.Double:
						return reader.ReadDouble();
					case PrimitiveType.Int16:
						return reader.ReadInt16();
					case PrimitiveType.Int32:
						return reader.ReadInt32();
					case PrimitiveType.Int64:
						return reader.ReadInt64();
					case PrimitiveType.SByte:
						return reader.ReadSByte();
					case PrimitiveType.Single:
						return reader.ReadSingle();
					case PrimitiveType.TimeSpan:
						return new TimeSpan(reader.ReadInt64());
					case PrimitiveType.DateTime:
						return DateTime.FromBinary(reader.ReadInt64());
					case PrimitiveType.UInt16:
						return reader.ReadUInt16();
					case PrimitiveType.UInt32:
						return reader.ReadUInt32();
					case PrimitiveType.UInt64:
						return reader.ReadUInt64();
					case PrimitiveType.Null:
						return null;
					case PrimitiveType.String:
						return ReadString();
					default:
						throw new InvalidDataException($"Unsupported primitive type '{(int)type}'!");
				}
			}

			/// <summary>
			/// Reads a length-prefixed UTF-8 string (7-bit encoded length prefix).
			/// </summary>
			private string ReadString()
			{
				var length = 0;
				var shift = 0;

				while (true)
				{
					var b = reader.ReadByte();

					length |= (b & 0x7f) << shift;

					if ((b & 0x80) == 0)
					{
						break;
					}

					shift += 7;
				}

				return Encoding.UTF8.GetString(reader.ReadBytes(length));
			}

			private object Materialize(object value)
			{
				switch (value)
				{
					case null:
						return null;
					case MemberReference reference:
						return MaterializeReference(reference);
					case ClassInstance instance:
						return MaterializeInstance(instance);
					case ArrayValue array:
						return MaterializeArray(array);
					default:
						return value;
				}
			}

			private object MaterializeReference(MemberReference reference)
			{
				if (!objects.TryGetValue(reference.ObjectId, out var value))
				{
					throw new InvalidDataException($"The stream references object ID {reference.ObjectId}, which has not been defined!");
				}

				if (!materializationChain.Add(reference.ObjectId))
				{
					throw new InvalidDataException($"The object graph contains a circular reference at object ID {reference.ObjectId}!");
				}

				try
				{
					return Materialize(value);
				}
				finally
				{
					materializationChain.Remove(reference.ObjectId);
				}
			}

			private object MaterializeInstance(ClassInstance instance)
			{
				var name = instance.Metadata.Name;

				if (name.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal))
				{
					return MaterializeDictionary(instance);
				}

				if (name.StartsWith("System.Collections.Generic.KeyValuePair`2", StringComparison.Ordinal))
				{
					return new KeyValuePair<string, object>((string)Materialize(instance.Values[0]), Materialize(instance.Values[1]));
				}

				if (name.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
				{
					return MaterializeList(instance);
				}

				throw new NotSupportedException($"Unexpected class '{name}' in the object graph!");
			}

			private IDictionary<string, object> MaterializeDictionary(ClassInstance instance)
			{
				var dictionary = new Dictionary<string, object>();
				var index = Array.IndexOf(instance.Metadata.MemberNames, "KeyValuePairs");

				if (index < 0)
				{
					// Empty dictionaries omit the KeyValuePairs member entirely.
					return dictionary;
				}

				foreach (var entry in (IEnumerable<object>)Materialize(instance.Values[index]))
				{
					var pair = (KeyValuePair<string, object>)entry;

					dictionary[pair.Key] = pair.Value;
				}

				return dictionary;
			}

			private object MaterializeList(ClassInstance instance)
			{
				var items = (object[])Materialize(instance.Values[Array.IndexOf(instance.Metadata.MemberNames, "_items")]);
				var size = (int)Materialize(instance.Values[Array.IndexOf(instance.Metadata.MemberNames, "_size")]);
				var list = new List<object>(size);

				for (var i = 0; i < size; i++)
				{
					list.Add(items[i]);
				}

				return list;
			}

			private object MaterializeArray(ArrayValue array)
			{
				if (array.ElementType == BinaryType.Primitive)
				{
					var result = Array.CreateInstance(GetClrType(array.PrimitiveType.Value), array.Values.Count);

					for (var i = 0; i < array.Values.Count; i++)
					{
						result.SetValue(array.Values[i], i);
					}

					return result;
				}

				var values = new object[array.Values.Count];

				for (var i = 0; i < array.Values.Count; i++)
				{
					values[i] = Materialize(array.Values[i]);
				}

				return values;
			}

			private Type GetClrType(PrimitiveType type)
			{
				switch (type)
				{
					case PrimitiveType.Boolean:
						return typeof(bool);
					case PrimitiveType.Byte:
						return typeof(byte);
					case PrimitiveType.Char:
						return typeof(char);
					case PrimitiveType.Currency:
					case PrimitiveType.Decimal:
						return typeof(decimal);
					case PrimitiveType.Double:
						return typeof(double);
					case PrimitiveType.Int16:
						return typeof(short);
					case PrimitiveType.Int32:
						return typeof(int);
					case PrimitiveType.Int64:
						return typeof(long);
					case PrimitiveType.SByte:
						return typeof(sbyte);
					case PrimitiveType.Single:
						return typeof(float);
					case PrimitiveType.TimeSpan:
						return typeof(TimeSpan);
					case PrimitiveType.DateTime:
						return typeof(DateTime);
					case PrimitiveType.UInt16:
						return typeof(ushort);
					case PrimitiveType.UInt32:
						return typeof(uint);
					case PrimitiveType.UInt64:
						return typeof(ulong);
					case PrimitiveType.String:
						return typeof(string);
					default:
						return typeof(object);
				}
			}
		}

		/// <summary>
		/// BinaryTypeEnum of the BinaryFormatter wire format.
		/// </summary>
		private enum BinaryType : byte
		{
			Primitive = 0,
			String = 1,
			Object = 2,
			SystemClass = 3,
			Class = 4,
			ObjectArray = 5,
			StringArray = 6,
			PrimitiveArray = 7
		}

		/// <summary>
		/// PrimitiveTypeEnum of the BinaryFormatter wire format (values of InternalPrimitiveTypeE).
		/// </summary>
		private enum PrimitiveType : byte
		{
			Invalid = 0,
			Boolean = 1,
			Byte = 2,
			Char = 3,
			Currency = 4,
			Decimal = 5,
			Double = 6,
			Int16 = 7,
			Int32 = 8,
			Int64 = 9,
			SByte = 10,
			Single = 11,
			TimeSpan = 12,
			DateTime = 13,
			UInt16 = 14,
			UInt32 = 15,
			UInt64 = 16,
			Null = 17,
			String = 18
		}

		private sealed class HeaderInfo
		{
			internal HeaderInfo(int rootId, int headerId)
			{
				RootId = rootId;
				HeaderId = headerId;
			}

			internal int RootId { get; }
			internal int HeaderId { get; }
		}

		private sealed class ClassMetadata
		{
			internal ClassMetadata(string name, string[] memberNames)
			{
				Name = name;
				MemberNames = memberNames;
			}

			internal string Name { get; }
			internal string[] MemberNames { get; }
			internal BinaryType[] BinaryTypes { get; set; }
			internal PrimitiveType[] PrimitiveTypes { get; set; }
			internal string[] TypeNames { get; set; }
		}

		private sealed class ClassInstance
		{
			internal ClassInstance(ClassMetadata metadata, object[] values)
			{
				Metadata = metadata;
				Values = values;
			}

			internal ClassMetadata Metadata { get; }
			internal object[] Values { get; }
		}

		/// <summary>
		/// A member value referencing a previously (or subsequently) defined object by ID.
		/// </summary>
		private sealed class MemberReference
		{
			internal MemberReference(int objectId)
			{
				ObjectId = objectId;
			}

			internal int ObjectId { get; }
		}

		/// <summary>
		/// A run of consecutive null elements (ObjectNullMultiple / ObjectNullMultiple256).
		/// </summary>
		private sealed class NullRun
		{
			internal NullRun(int count)
			{
				Count = count;
			}

			internal int Count { get; }
		}

		/// <summary>
		/// A serialized array (BinaryArray / ArraySinglePrimitive / ArraySingleObject / ArraySingleString).
		/// </summary>
		private sealed class ArrayValue
		{
			internal BinaryType ElementType { get; set; }
			internal PrimitiveType? PrimitiveType { get; set; }
			internal string TypeName { get; set; }
			internal List<object> Values { get; set; }
		}
	}
}
