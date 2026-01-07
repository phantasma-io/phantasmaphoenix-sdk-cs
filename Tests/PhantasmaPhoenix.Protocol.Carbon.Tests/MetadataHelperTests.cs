using System.Numerics;
using Shouldly;
using Xunit;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class MetadataHelperTests
{
	private static VmNamedVariableSchema CreateSchema(string name, VmType type, VmStructSchema? structSchema = null)
	{
		var schema = new VmVariableSchema { type = type };
		if (structSchema.HasValue)
		{
			schema.structure = structSchema.Value;
		}

		return new VmNamedVariableSchema
		{
			name = new SmallString(name),
			schema = schema
		};
	}

	private static VmStructSchema CreateStructSchema(params VmNamedVariableSchema[] fields)
	{
		return new VmStructSchema
		{
			fields = fields,
			flags = VmStructSchema.Flags.None
		};
	}

	[Fact]
	public void Accepts_Matching_Int32_Values()
	{
		var schema = CreateSchema("royalties", VmType.Int32);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("royalties", 42) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		fields.Count.ShouldBe(1);
		fields[0].value.type.ShouldBe(VmType.Int32);
		((int)fields[0].value.data!).ShouldBe(42);
	}

	[Fact]
	public void Rejects_NonInteger_Int32_Values()
	{
		var schema = CreateSchema("royalties", VmType.Int32);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("royalties", "forty-two") };

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("Metadata field 'royalties' must be an integer between -2147483648 and 2147483647");
	}

	[Fact]
	public void Rejects_Int32_Out_Of_Range_Values()
	{
		var schema = CreateSchema("royalties", VmType.Int32);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("royalties", 1L << 32) };

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("Metadata field 'royalties' must be between -2147483648 and 2147483647 or between 0 and 4294967295");
	}

	[Fact]
	public void Accepts_Hex_Strings_For_Byte_Fields()
	{
		var schema = CreateSchema("payload", VmType.Bytes);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("payload", "0a0b") };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		fields[0].value.type.ShouldBe(VmType.Bytes);
		((byte[])fields[0].value.data!).ShouldBe(new byte[] { 0x0a, 0x0b });
	}

	[Fact]
	public void Accepts_0x_Prefixed_Hex_Strings_For_Byte_Fields()
	{
		var schema = CreateSchema("payload", VmType.Bytes);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("payload", "0x0a0b") };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		fields[0].value.type.ShouldBe(VmType.Bytes);
		((byte[])fields[0].value.data!).ShouldBe(new byte[] { 0x0a, 0x0b });
	}

	[Fact]
	public void Accepts_Unsigned_Range_Values_For_Int8()
	{
		var schema = CreateSchema("level", VmType.Int8);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("level", 200) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((byte)fields[0].value.data!).ShouldBe((byte)200);
	}

	[Fact]
	public void Accepts_Unsigned_Range_Values_For_Int16()
	{
		var schema = CreateSchema("checksum", VmType.Int16);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("checksum", 65535) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((ushort)(short)fields[0].value.data!).ShouldBe((ushort)65535);
	}

	[Fact]
	public void Rejects_Invalid_Hex_Strings_For_Byte_Fields()
	{
		var schema = CreateSchema("payload", VmType.Bytes);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("payload", "xyz") };

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("Metadata field 'payload' must be a byte array or hex string");
	}

	[Fact]
	public void Rejects_NonInteger_Int64_Values()
	{
		var schema = CreateSchema("supply", VmType.Int64);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("supply", 1.5) };

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("Metadata field 'supply' must be a bigint or a safe integer number");
	}

	[Fact]
	public void Accepts_BigInteger_Input_For_Int64()
	{
		var schema = CreateSchema("supply", VmType.Int64);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("supply", new BigInteger(123)) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((long)fields[0].value.data!).ShouldBe(123L);
	}

	[Fact]
	public void Rejects_Int64_Out_Of_Range_Values()
	{
		var schema = CreateSchema("supply", VmType.Int64);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("supply", BigInteger.One << 64) };

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("(Int64)");
	}

	[Fact]
	public void Rejects_Int256_Out_Of_Range_Values()
	{
		var schema = CreateSchema("value", VmType.Int256);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("value", BigInteger.One << 256) };

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("(Int256)");
	}

	[Fact]
	public void Accepts_Unsigned_Range_Values_For_Int64()
	{
		var schema = CreateSchema("supply", VmType.Int64);
		var fields = new List<VmNamedDynamicVariable>();
		var maxUnsigned = (BigInteger.One << 64) - 1;
		var metadata = new List<MetadataField> { new("supply", maxUnsigned) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((ulong)(long)fields[0].value.data!).ShouldBe(ulong.MaxValue);
	}

	[Fact]
	public void Accepts_Unsigned_Range_Values_For_Int256()
	{
		var schema = CreateSchema("value", VmType.Int256);
		var fields = new List<VmNamedDynamicVariable>();
		var maxUnsigned = (BigInteger.One << 256) - 1;
		var metadata = new List<MetadataField> { new("value", maxUnsigned) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((BigInteger)fields[0].value.data!).ShouldBe(maxUnsigned);
	}

	[Fact]
	public void Builds_Nested_Struct_Metadata_From_Dictionary()
	{
		var nestedSchema = CreateStructSchema(
			CreateSchema("innerName", VmType.String),
			CreateSchema("innerValue", VmType.Int32));
		var schema = CreateSchema("details", VmType.Struct, nestedSchema);

		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField>
		{
			new("details", new Dictionary<string, object?>
			{
				{ "innerName", "demo" },
				{ "innerValue", 5 }
			})
		};

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		var nested = (VmDynamicStruct)fields[0].value.data!;
		nested.fields.Length.ShouldBe(2);
		nested.fields[0].name.data.ShouldBe("innerName");
		((string)nested.fields[0].value.data!).ShouldBe("demo");
		((int)nested.fields[1].value.data!).ShouldBe(5);
	}

	[Fact]
	public void Rejects_Struct_Metadata_With_Unknown_Properties()
	{
		var nestedSchema = CreateStructSchema(CreateSchema("innerName", VmType.String));
		var schema = CreateSchema("details", VmType.Struct, nestedSchema);

		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField>
		{
			new("details", new Dictionary<string, object?>
			{
				{ "innerName", "demo" },
				{ "extra", "oops" }
			})
		};

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("Metadata field 'details' received unknown property 'extra'");
	}

	[Fact]
	public void Rejects_Struct_Metadata_Missing_Required_Fields()
	{
		var nestedSchema = CreateStructSchema(CreateSchema("innerName", VmType.String));
		var schema = CreateSchema("details", VmType.Struct, nestedSchema);

		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField>
		{
			new("details", new Dictionary<string, object?>())
		};

		var ex = Assert.Throws<ArgumentException>(() => MetadataHelper.PushMetadataField(schema, fields, metadata));
		ex.Message.ShouldContain("Metadata field 'details.innerName' is mandatory");
	}

	[Fact]
	public void Accepts_Arrays_For_Matching_Array_Schema()
	{
		var schema = CreateSchema("tags", VmType.Array | VmType.String);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("tags", new[] { "alpha", "beta" }) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((string[])fields[0].value.data!).ShouldBe(new[] { "alpha", "beta" });
	}

	[Fact]
	public void Converts_Int8_Arrays_Into_Bytes()
	{
		var schema = CreateSchema("deltas", VmType.Array | VmType.Int8);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("deltas", new[] { 1, -1, 5 }) };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		((byte[])fields[0].value.data!).ShouldBe(new byte[] { 1, 255, 5 });
	}

	[Fact]
	public void Builds_StructArray_For_Arrays_Of_Structs()
	{
		var elementSchema = CreateStructSchema(CreateSchema("name", VmType.String));
		var schema = CreateSchema("items", VmType.Array | VmType.Struct, elementSchema);

		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField>
		{
			new("items", new object[]
			{
				new Dictionary<string, object?> { { "name", "one" } },
				new Dictionary<string, object?> { { "name", "two" } }
			})
		};

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		var arrayValue = (VmStructArray)fields[0].value.data!;
		arrayValue.schema.fields.Length.ShouldBe(1);
		arrayValue.schema.fields[0].name.data.ShouldBe("name");
		arrayValue.structs.Length.ShouldBe(2);
	}

	[Fact]
	public void Converts_Hex_Input_Into_Bytes16_Instance()
	{
		var schema = CreateSchema("hash", VmType.Bytes16);
		var fields = new List<VmNamedDynamicVariable>();
		var metadata = new List<MetadataField> { new("hash", "00112233445566778899aabbccddeeff") };

		MetadataHelper.PushMetadataField(schema, fields, metadata);

		fields[0].value.data.ShouldBeOfType<Bytes16>();
		((Bytes16)fields[0].value.data!).bytes.Length.ShouldBe(16);
	}
}
