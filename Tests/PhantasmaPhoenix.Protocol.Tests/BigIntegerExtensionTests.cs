using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

public class BigIntegerExtensionTests
{
	[Fact]
	public void ToUnsignedByteArray_strips_sign_guards_and_zero()
	{
		// Zero should return an empty array.
		BigInteger.Zero.ToUnsignedByteArray().ShouldBe(Array.Empty<byte>());

		// Positive values should strip the sign guard (0x00).
		new BigInteger(128).ToUnsignedByteArray().ShouldBe(new byte[] { 0x80 });

		// Negative values should return magnitude bytes only.
		new BigInteger(-1).ToUnsignedByteArray().ShouldBe(new byte[] { 0x01 });

		// Multi-byte values are little-endian.
		new BigInteger(0x12345678).ToUnsignedByteArray()
			.ShouldBe(new byte[] { 0x78, 0x56, 0x34, 0x12 });
	}

	[Fact]
	public void ToSignedByteArray_adds_expected_guards()
	{
		// Zero uses a single 0x00 byte.
		BigInteger.Zero.ToSignedByteArray().ShouldBe(new byte[] { 0x00 });

		// Positive values append a 0x00 guard when needed.
		new BigInteger(1).ToSignedByteArray().ShouldBe(new byte[] { 0x01, 0x00 });
		new BigInteger(128).ToSignedByteArray().ShouldBe(new byte[] { 0x80, 0x00 });
		new BigInteger(0x12345678).ToSignedByteArray()
			.ShouldBe(new byte[] { 0x78, 0x56, 0x34, 0x12, 0x00 });

		// Negative values extend with 0xFF guards for two's complement.
		new BigInteger(-1).ToSignedByteArray().ShouldBe(new byte[] { 0xFF, 0xFF, 0xFF });
		new BigInteger(-255).ToSignedByteArray().ShouldBe(new byte[] { 0x01, 0xFF, 0xFF });
	}

	[Fact]
	public void Bit_helpers_match_expected_positions()
	{
		// TestBit should report set bits on the little-endian representation.
		var value = new BigInteger(42); // 0b101010
		value.TestBit(0).ShouldBeFalse();
		value.TestBit(1).ShouldBeTrue();
		value.TestBit(2).ShouldBeFalse();
		value.TestBit(3).ShouldBeTrue();
		value.TestBit(4).ShouldBeFalse();
		value.TestBit(5).ShouldBeTrue();

		// GetLowestSetBit should return -1 for zero and the lowest set bit index otherwise.
		BigInteger.Zero.GetLowestSetBit().ShouldBe(-1);
		new BigInteger(1).GetLowestSetBit().ShouldBe(0);
		new BigInteger(2).GetLowestSetBit().ShouldBe(1);
		new BigInteger(40).GetLowestSetBit().ShouldBe(3);
	}

	[Fact]
	public void ModInverse_returns_multiplicative_inverse()
	{
		// Known inverses in small rings.
		new BigInteger(3).ModInverse(11).ShouldBe(new BigInteger(4));
		new BigInteger(10).ModInverse(17).ShouldBe(new BigInteger(12));
	}

	[Fact]
	public void HexToBigInteger_parses_hex_strings()
	{
		// Hex parsing should accept leading zeros and mixed case.
		"FF".HexToBigInteger().ShouldBe(new BigInteger(255));
		"00ff".HexToBigInteger().ShouldBe(new BigInteger(255));
		"deadbeef".HexToBigInteger().ShouldBe(new BigInteger(3735928559));
	}

	[Fact]
	public void IsParsable_accepts_decimal_digits_only()
	{
		// Only ASCII digits should return true.
		"12345".IsParsable().ShouldBeTrue();
		"12a45".IsParsable().ShouldBeFalse();
		"".IsParsable().ShouldBeFalse();
	}
}
