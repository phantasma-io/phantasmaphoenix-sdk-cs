using PhantasmaPhoenix.Cryptography;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Cryptography.Tests;

public class EncodingTests
{
	[Fact]
	public void Base16_roundtrip_preserves_bytes()
	{
		var data = new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0xFF, 0x10, 0x20 };

		var encoded = Base16.Encode(data);
		var decoded = Base16.Decode(encoded);

		decoded.ShouldNotBeNull();
		decoded.ShouldBe(data);
	}

	[Fact]
	public void Base58_roundtrip_preserves_bytes()
	{
		var data = new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0xFF, 0x10, 0x20 };

		var encoded = Base58.Encode(data);
		var decoded = Base58.Decode(encoded);

		decoded.ShouldBe(data);
	}
}
