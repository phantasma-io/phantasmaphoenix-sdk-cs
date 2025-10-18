using System.Numerics;
using Shouldly;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class VmDynamicVariableTests
{
	private static VmDynamicVariable Roundtrip(VmDynamicVariable input, VmType asType)
	{
		using var ms = new MemoryStream();
		using var w = new BinaryWriter(ms);
		// write the typed value only (like inner payload)
		VmDynamicVariable.Write(asType, input, null, w);
		var buf = ms.ToArray();

		using var ms2 = new MemoryStream(buf);
		using var r = new BinaryReader(ms2);
		VmDynamicVariable output;
		VmDynamicVariable.Read(asType, out output, null, r);
		return output;
	}

	[Theory]
	[InlineData((byte)0)]
	[InlineData((byte)1)]
	[InlineData(byte.MaxValue)]
	public void Int8_Roundtrip(byte value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int8);
		round.type.ShouldBe(VmType.Int8);
		((byte)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((sbyte)0)]
	[InlineData((sbyte)1)]
	[InlineData(sbyte.MinValue)]
	[InlineData(sbyte.MaxValue)]
	public void UInt8_Roundtrip(sbyte value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int8);
		round.type.ShouldBe(VmType.Int8);
		((sbyte)(byte)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((UInt16)0)]
	[InlineData((UInt16)1)]
	[InlineData(UInt16.MaxValue)]
	public void UInt16_Roundtrip(UInt16 value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int16);
		round.type.ShouldBe(VmType.Int16);
		((UInt16)(Int16)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((Int16)0)]
	[InlineData((Int16)1)]
	[InlineData(Int16.MinValue)]
	[InlineData(Int16.MaxValue)]
	public void Int16_Roundtrip(Int16 value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int16);
		round.type.ShouldBe(VmType.Int16);
		((Int16)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((UInt32)0)]
	[InlineData((UInt32)1)]
	[InlineData(UInt32.MaxValue)]
	public void UInt32_Roundtrip(UInt32 value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int32);
		round.type.ShouldBe(VmType.Int32);
		((UInt32)(Int32)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((Int32)0)]
	[InlineData((Int32)1)]
	[InlineData(Int32.MinValue)]
	[InlineData(Int32.MaxValue)]
	public void Int32_Roundtrip(Int32 value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int32);
		round.type.ShouldBe(VmType.Int32);
		((Int32)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((UInt64)0L)]
	[InlineData((UInt64)1L)]
	[InlineData(UInt64.MaxValue)]
	public void UInt64_Roundtrip(UInt64 value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int64);
		round.type.ShouldBe(VmType.Int64);
		((UInt64)(Int64)round.data!).ShouldBe(value);
	}

	[Theory]
	[InlineData((Int64)0L)]
	[InlineData((Int64)1L)]
	[InlineData(Int64.MinValue)]
	[InlineData(Int64.MaxValue)]
	public void Int64_Roundtrip(Int64 value)
	{
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int64);
		round.type.ShouldBe(VmType.Int64);
		((Int64)round.data!).ShouldBe(value);
	}

	[Fact]
	public void Int256_Roundtrip()
	{
		var value = BigInteger.Parse("1234567890123456789012345678901234567890");
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Int256);
		round.type.ShouldBe(VmType.Int256);
		((BigInteger)round.data!).ShouldBe(value);
	}

	[Fact]
	public void Bytes_Roundtrip()
	{
		var value = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Bytes);
		round.type.ShouldBe(VmType.Bytes);
		((byte[])round.data!).ShouldBe(value);
	}

	[Fact]
	public void String_Roundtrip()
	{
		var value = "hello world";
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.String);
		round.type.ShouldBe(VmType.String);
		((string)round.data!).ShouldBe(value);
	}

	[Fact]
	public void Bytes16_Roundtrip()
	{
		var value = new Bytes16(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Bytes16);
		round.type.ShouldBe(VmType.Bytes16);
		((Bytes16)round.data!).ShouldBe(value);
	}

	[Fact]
	public void Bytes32_Roundtrip()
	{
		var value = new Bytes32(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Bytes32);
		round.type.ShouldBe(VmType.Bytes32);
		((Bytes32)round.data!).ShouldBe(value);
	}

	[Fact]
	public void Bytes64_Roundtrip()
	{
		var value = new Bytes64(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
		var v = new VmDynamicVariable(value);
		var round = Roundtrip(v, VmType.Bytes64);
		round.type.ShouldBe(VmType.Bytes64);
		((Bytes64)round.data!).ShouldBe(value);
	}
}
