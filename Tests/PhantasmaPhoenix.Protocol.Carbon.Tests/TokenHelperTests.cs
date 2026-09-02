using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class TokenHelperTests
{
	[Fact]
	public void Round_trips_an_NFT_address_through_pack_and_unpack()
	{
		var address = TokenHelper.GetNftAddress(9, (5UL << 32) | 3);
		TokenHelper.UnpackNftInstanceId((5UL << 32) | 3, out var seriesId, out var mintNumber);
		seriesId.ShouldBe(3u);
		mintNumber.ShouldBe(5u);
		BitConverter.ToUInt64(address.bytes, 16).ShouldBe(9UL);
		BitConverter.ToUInt64(address.bytes, 24).ShouldBe((5UL << 32) | 3);
	}

	// The form is what the chain tests: fifteen zero bytes, a 0x01 marker, then a nonzero token id
	// and instance id. A user key, the null address and the zero-id shapes all fail it.
	[Fact]
	public void Recognises_the_NFT_address_form_and_nothing_else()
	{
		TokenHelper.IsNftAddress(TokenHelper.GetNftAddress(9, 1)).ShouldBeTrue();
		TokenHelper.IsNftAddress(TokenHelper.GetNftAddress(9, 1).bytes).ShouldBeTrue();

		TokenHelper.IsNftAddress(new Bytes32(PhantasmaKeys.Generate().PublicKey)).ShouldBeFalse();
		TokenHelper.IsNftAddress(Bytes32.Empty).ShouldBeFalse();
		TokenHelper.IsNftAddress(TokenHelper.GetNftAddress(0, 1)).ShouldBeFalse();
		TokenHelper.IsNftAddress(TokenHelper.GetNftAddress(9, 0)).ShouldBeFalse();
		TokenHelper.IsNftAddress(new byte[16]).ShouldBeFalse();

		// A nonzero byte anywhere in the leading run breaks the form even with the marker in place.
		var smudged = (byte[])TokenHelper.GetNftAddress(9, 1).bytes.Clone();
		smudged[3] = 1;
		TokenHelper.IsNftAddress(smudged).ShouldBeFalse();
	}
}
