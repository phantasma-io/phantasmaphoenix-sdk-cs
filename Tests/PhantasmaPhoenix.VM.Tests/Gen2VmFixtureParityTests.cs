using System.Globalization;
using System.Numerics;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.VM.Tests;

public class Gen2VmFixtureParityTests
{
	private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

	public static IEnumerable<object[]> BigIntegerBinaryCases => ReadRows("gen2_csharp_vm_bigint_binary.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> BigIntegerDecimalCases => ReadRows("gen2_csharp_vm_bigint_decimal.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> BigIntegerNarrowCases => ReadRows("gen2_csharp_vm_bigint_narrow_int.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> BigIntegerOpCases => ReadRows("gen2_csharp_vm_bigint_ops.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> BigIntegerUnaryCases => ReadRows("gen2_csharp_vm_bigint_unary_ops.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> PhantasmaBigIntegerCases => ReadRows("phantasma_bigint_vectors.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> ScriptContextUnaryCases => ReadRows("gen2_csharp_vm_scriptcontext_unary.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectAsNumberCases => ReadRows("gen2_csharp_vmobject_asnumber.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectAsBytesCases => ReadRows("gen2_csharp_vmobject_asbytes.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectAsBoolCases => ReadRows("gen2_csharp_vmobject_asbool.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectAsStringCases => ReadRows("gen2_csharp_vmobject_asstring.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectArrayTypeCases => ReadRows("gen2_csharp_vmobject_arraytype.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectSerdeCases => ReadRows("gen2_csharp_vmobject_serde.tsv").Select(row => new object[] { row });
	public static IEnumerable<object[]> VMObjectCastStructCases => ReadRows("gen2_csharp_vmobject_cast_struct.tsv").Select(row => new object[] { row });

	[Theory]
	[MemberData(nameof(BigIntegerBinaryCases))]
	public void BigInteger_binary_fixture_matches_sdk_codecs(TsvRow row)
	{
		// Binary fixture rows lock the public BigInteger byte codecs and the
		// ScriptBuilder load encoding against the shared VM reference vectors.
		var value = BigInteger.Parse(row["value"], CultureInfo.InvariantCulture);

		Hex(value.ToSignedByteArray()).ShouldBe(row["signed_hex"]);
		Hex(value.ToUnsignedByteArray()).ShouldBe(row["unsigned_hex"]);

		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream))
		{
			writer.WriteBigInteger(value);
		}
		Hex(stream.ToArray()).ShouldBe(row["io_write_hex"]);

		using var readStream = new MemoryStream(stream.ToArray());
		using var reader = new BinaryReader(readStream);
		reader.ReadBigInteger().ShouldBe(value);

		var builder = new ScriptBuilder();
		builder.EmitLoad(0, value);
		Hex(builder.ToScript()).ShouldBe(row["script_load_hex"]);
	}

	[Theory]
	[MemberData(nameof(BigIntegerDecimalCases))]
	public void BigInteger_decimal_fixture_matches_dotnet_parser(TsvRow row)
	{
		// Decimal rows document parser acceptance and rejection exactly; ok rows
		// must normalize to the fixture value and fault rows must throw.
		var result = Call(() => BigInteger.Parse(row["input_text"], CultureInfo.InvariantCulture));

		if (row["outcome"] == "ok")
		{
			result.ShouldBe(BigInteger.Parse(row["normalized_decimal"], CultureInfo.InvariantCulture));
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	[Theory]
	[MemberData(nameof(BigIntegerNarrowCases))]
	public void BigInteger_narrow_int_fixture_matches_sdk_cast(TsvRow row)
	{
		// Narrow integer rows guard the SDK's checked/overflow behavior when VM
		// numbers cross a public .NET int boundary.
		var value = BigInteger.Parse(row["value"], CultureInfo.InvariantCulture);
		var result = Call(() => (int)value);

		if (row["outcome"] == "ok")
		{
			result.ShouldBe(int.Parse(row["int_value"], CultureInfo.InvariantCulture));
		}
		else
		{
			result.ShouldBeAssignableTo<OverflowException>();
		}
	}

	[Theory]
	[MemberData(nameof(BigIntegerOpCases))]
	public void BigInteger_binary_op_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Direct BigInteger operation rows keep arithmetic expectations aligned
		// with the shared fixture set used by the validator and other SDKs.
		var a = BigInteger.Parse(row["a"], CultureInfo.InvariantCulture);
		var b = BigInteger.Parse(row["b"], CultureInfo.InvariantCulture);
		var result = Call(() => EvalBigIntegerOp(row["op"], a, b));

		if (row["outcome"] == "ok")
		{
			result.ShouldBe(BigInteger.Parse(row["value"], CultureInfo.InvariantCulture));
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	[Theory]
	[MemberData(nameof(BigIntegerUnaryCases))]
	public void BigInteger_unary_op_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Unary rows verify sign/negation/absolute-value behavior without going
		// through a separate helper that could mask SDK semantic drift.
		var a = BigInteger.Parse(row["a"], CultureInfo.InvariantCulture);
		var result = Call(() => EvalBigIntegerUnaryOp(row["op"], a));

		if (row["outcome"] == "ok")
		{
			result.ShouldBe(BigInteger.Parse(row["value"], CultureInfo.InvariantCulture));
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	[Theory]
	[MemberData(nameof(PhantasmaBigIntegerCases))]
	public void Phantasma_BigInteger_fixture_matches_sdk_byte_encodings(TsvRow row)
	{
		// These rows compare Phantasma and C# byte encodings and then read the
		// C# encoding back, catching endian/sign-extension regressions.
		var value = BigInteger.Parse(row["number"], CultureInfo.InvariantCulture);

		value.ToSignedByteArray().ShouldBe(DecimalBytes(row["pha"]));
		value.ToByteArray().ShouldBe(DecimalBytes(row["csharp"]));
		BigInteger.Parse(row["number"], CultureInfo.InvariantCulture).ShouldBe(new BigInteger(DecimalBytes(row["csharp"])));
	}

	[Theory]
	[MemberData(nameof(ScriptContextUnaryCases))]
	public void ScriptContext_unary_fixture_matches_vm_execution(TsvRow row)
	{
		// Unary ScriptContext rows execute real VM bytecode and compare the top
		// register descriptor or expected fault path from the shared fixtures.
		var script = BuildUnaryScript(row);
		var vm = new GasMachine(script, 0);

		if (row["outcome"] == "ok")
		{
			vm.Execute().ShouldBe(ExecutionState.Halt);
			Describe(vm.CurrentFrame.Registers[1]).ShouldBe(row["top_descriptor"]);
		}
		else
		{
			Should.Throw<VMException>(() => vm.Execute());
			vm.CurrentContext.ShouldBeOfType<ScriptContext>().Error.ShouldBe("Generic Error");
		}
	}

	[Theory]
	[MemberData(nameof(VMObjectAsNumberCases))]
	public void VMObject_as_number_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Numeric conversion rows include None and object/hash edge cases that
		// previously diverged from the shared VM fixture semantics.
		var result = Call(() => ObjectFromFixture(row["source_kind"], row["payload"]).AsNumber());

		if (row["outcome"] == "ok")
		{
			result.ShouldBe(BigInteger.Parse(row["value"], CultureInfo.InvariantCulture));
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	[Theory]
	[MemberData(nameof(VMObjectAsBytesCases))]
	public void VMObject_as_bytes_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Byte conversion rows verify both successful payload conversion and the
		// rejection paths for incompatible VMObject source kinds.
		var result = Call(() => ObjectFromFixture(row["source_kind"], row["payload"]).AsByteArray());

		if (row["outcome"] == "ok")
		{
			Hex((byte[])result).ShouldBe(row["bytes_hex"]);
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	[Theory]
	[MemberData(nameof(VMObjectAsBoolCases))]
	public void VMObject_as_bool_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Bool conversion rows lock the SDK's accepted VMObject inputs and keep
		// invalid conversions observable as exceptions.
		var result = Call(() => ObjectFromFixture(row["source_kind"], row["payload"]).AsBool());

		if (row["outcome"] == "ok")
		{
			((bool)result).ToString().ToLowerInvariant().ShouldBe(row["value"]);
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	[Theory]
	[MemberData(nameof(VMObjectAsStringCases))]
	public void VMObject_as_string_fixture_matches_sdk_semantics(TsvRow row)
	{
		// String rows keep textual VMObject conversion behavior aligned with the
		// shared reference descriptors.
		ObjectFromFixture(row["source_kind"], row["payload"]).AsString().ShouldBe(row["value"]);
	}

	[Theory]
	[MemberData(nameof(VMObjectArrayTypeCases))]
	public void VMObject_array_type_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Array-type rows guard the VMObject type inference used by contract ABI
		// and script serialization code.
		ObjectFromFixture(row["source_kind"], row["payload"]).GetArrayType().ToString().ShouldBe(row["array_type"]);
	}

	[Theory]
	[MemberData(nameof(VMObjectSerdeCases))]
	public void VMObject_serde_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Serde rows prove both write bytes and read-back descriptors, so the
		// test can catch asymmetric VMObject serialization changes.
		var obj = ObjectFromFixture(row["source_kind"], row["payload"]);
		SerializeObject(obj).ShouldBe(row["serialized_hex"]);

		var roundTrip = VMObject.FromBytes(Bytes(row["serialized_hex"]));
		roundTrip.Type.ToString().ShouldBe(row["roundtrip_type"]);
		Describe(roundTrip).ShouldBe(row["roundtrip_descriptor"]);
	}

	[Theory]
	[MemberData(nameof(VMObjectCastStructCases))]
	public void VMObject_cast_struct_fixture_matches_sdk_semantics(TsvRow row)
	{
		// Struct cast rows verify accepted casts and rejection paths using real
		// VMObject.CastTo behavior instead of a fixture-only mirror.
		var result = Call(() => VMObject.CastTo(ObjectFromFixture(row["source_kind"], row["payload"]), VMType.Struct));

		if (row["outcome"] == "ok")
		{
			var obj = (VMObject)result;
			obj.Type.ToString().ShouldBe(row["result_type"]);
			Describe(obj).ShouldBe(row["result_descriptor"]);
		}
		else
		{
			result.ShouldBeAssignableTo<Exception>();
		}
	}

	private static BigInteger EvalBigIntegerOp(string op, BigInteger a, BigInteger b)
	{
		return op switch
		{
			"ADD" => a + b,
			"SUB" => a - b,
			"MUL" => a * b,
			"DIV" => a / b,
			"MOD" => a % b,
			"SHR" => a >> (int)b,
			"SHL" => a << (int)b,
			"MIN" => a < b ? a : b,
			"MAX" => a > b ? a : b,
			"POW" => BigInteger.Pow(a, (int)b),
			_ => throw new InvalidOperationException($"unsupported fixture op {op}"),
		};
	}

	private static BigInteger EvalBigIntegerUnaryOp(string op, BigInteger a)
	{
		return op switch
		{
			"SIGN" => a.Sign,
			"NEGATE" => -a,
			"ABS" => BigInteger.Abs(a),
			_ => throw new InvalidOperationException($"unsupported fixture op {op}"),
		};
	}

	private static byte[] BuildUnaryScript(TsvRow row)
	{
		var builder = new ScriptBuilder();
		EmitLoad(builder, 0, row["source_kind"], row["source_payload"]);
		builder.Emit(Enum.Parse<Opcode>(row["op"]), new byte[] { 0, 1 });
		builder.Emit(Opcode.RET);
		return builder.ToScript();
	}

	private static void EmitLoad(ScriptBuilder builder, byte register, string kind, string payload)
	{
		switch (kind)
		{
			case "number":
				builder.EmitLoad(register, BigInteger.Parse(payload, CultureInfo.InvariantCulture));
				break;
			case "string":
				builder.EmitLoad(register, payload);
				break;
			case "bytes":
				builder.EmitLoad(register, Bytes(payload), VMType.Bytes);
				break;
			case "bool":
				builder.EmitLoad(register, bool.Parse(payload));
				break;
			default:
				throw new InvalidOperationException($"unsupported fixture operand kind {kind}");
		}
	}

	private static VMObject ObjectFromFixture(string sourceKind, string payload)
	{
		return sourceKind switch
		{
			"serialized_vmobject" => VMObject.FromBytes(Bytes(payload)),
			"empty" => new VMObject(),
			"string" => new VMObject().SetValue(payload),
			"bytes" => new VMObject().SetValue(Bytes(payload)),
			"bool" => new VMObject().SetValue(bool.Parse(payload)),
			"enum" => new VMObject().SetValue(BitConverter.GetBytes(uint.Parse(payload, CultureInfo.InvariantCulture)), VMType.Enum),
			"timestamp" => new VMObject().SetValue(new Timestamp(uint.Parse(payload, CultureInfo.InvariantCulture))),
			"number" => new VMObject().SetValue(BigInteger.Parse(payload, CultureInfo.InvariantCulture)),
			"object" => new VMObject().SetValue(Bytes(payload), VMType.Object),
			"struct" => SimpleStructObject(),
			_ => throw new InvalidOperationException($"unsupported fixture source kind {sourceKind}"),
		};
	}

	private static VMObject SimpleStructObject()
	{
		var children = new Dictionary<VMObject, VMObject>
		{
			[new VMObject().SetValue("name")] = new VMObject().SetValue("neo"),
			[new VMObject().SetValue("count")] = new VMObject().SetValue(new BigInteger(7)),
		};
		return new VMObject().SetValue(children);
	}

	private static string Describe(VMObject obj)
	{
		return obj.Type switch
		{
			VMType.None => "None",
			VMType.Struct => $"Struct:{SerializeObject(obj)}",
			VMType.Bytes => $"Bytes:{Hex(obj.AsByteArray())}",
			VMType.Number => $"Number:{obj.AsNumber()}",
			VMType.String => $"String:{obj.AsString()}",
			VMType.Timestamp => $"Timestamp:{obj.AsTimestamp().Value}",
			VMType.Bool => $"Bool:{obj.AsBool().ToString().ToLowerInvariant()}",
			VMType.Enum => $"Enum:{obj.AsNumber()}",
			VMType.Object when obj.Data is Address address => $"Object.Address:{Hex(address.ToByteArray())}",
			VMType.Object when obj.Data is Hash hash => $"Object.Hash:{Hex(hash.ToByteArray())}",
			VMType.Object when obj.Data is byte[] bytes => $"Object:{Hex(bytes)}",
			VMType.Object => $"Object:{obj.Data?.GetType().Name ?? "null"}",
			_ => throw new InvalidOperationException($"unsupported object type {obj.Type}"),
		};
	}

	private static string SerializeObject(VMObject obj)
	{
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream))
		{
			obj.SerializeData(writer);
		}
		return Hex(stream.ToArray());
	}

	private static object Call(Func<object> action)
	{
		try
		{
			return action();
		}
		catch (Exception ex)
		{
			return ex;
		}
	}

	private static IReadOnlyList<TsvRow> ReadRows(string fixtureName)
	{
		var path = Path.Combine(FixtureDir, fixtureName);
		string[]? header = null;
		var rows = new List<TsvRow>();

		foreach (var line in File.ReadLines(path))
		{
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
			{
				continue;
			}

			var parts = line.Split('\t');
			if (header == null)
			{
				header = parts;
				continue;
			}

			if (parts.Length < header.Length)
			{
				Array.Resize(ref parts, header.Length);
				for (var i = 0; i < parts.Length; i++)
				{
					parts[i] ??= string.Empty;
				}
			}

			parts.Length.ShouldBe(header.Length, $"wrong TSV width in {fixtureName}: {line}");
			rows.Add(new TsvRow(fixtureName, header, parts));
		}

		rows.Count.ShouldBeGreaterThan(0, $"empty TSV fixture {fixtureName}");
		return rows;
	}

	private static byte[] Bytes(string hex)
	{
		return string.IsNullOrEmpty(hex) ? Array.Empty<byte>() : Convert.FromHexString(hex);
	}

	private static byte[] DecimalBytes(string text)
	{
		return string.IsNullOrWhiteSpace(text)
			? Array.Empty<byte>()
			: text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(part => byte.Parse(part, CultureInfo.InvariantCulture))
				.ToArray();
	}

	private static string Hex(byte[] bytes)
	{
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}

	public sealed class TsvRow
	{
		private readonly Dictionary<string, string> _values;

		public TsvRow(string fixtureName, IReadOnlyList<string> header, IReadOnlyList<string> values)
		{
			FixtureName = fixtureName;
			_values = new Dictionary<string, string>(StringComparer.Ordinal);
			for (var i = 0; i < header.Count; i++)
			{
				_values[header[i]] = values[i];
			}
		}

		public string FixtureName { get; }

		public string this[string name] => _values[name];

		public override string ToString()
		{
			return _values.TryGetValue("case_id", out var caseId)
				? $"{FixtureName}:{caseId}"
				: $"{FixtureName}:{_values.First().Value}";
		}
	}
}
