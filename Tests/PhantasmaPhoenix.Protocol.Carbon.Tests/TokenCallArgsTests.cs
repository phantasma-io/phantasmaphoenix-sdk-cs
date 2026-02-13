using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class TokenCallArgsTests
{
	private const string TransferFungibleArgsHex =
		"1111111111111111111111111111111111111111111111111111111111111111222222222222222222222222222222222222222222222222222222222222222201000000000000000800E1F50500000000";

	[Fact]
	public void TransferFungibleArgs_vector_roundtrip()
	{
		var args = new TransferFungibleArgs
		{
			to = new Bytes32(Enumerable.Repeat((byte)0x11, 32).ToArray()),
			from = new Bytes32(Enumerable.Repeat((byte)0x22, 32).ToArray()),
			tokenId = 1,
			amount = new IntX(100000000)
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(TransferFungibleArgsHex);

		var decoded = CarbonBlob.New<TransferFungibleArgs>(TransferFungibleArgsHex.FromHex()!);
		decoded.to.Equals(args.to).ShouldBeTrue();
		decoded.from.Equals(args.from).ShouldBeTrue();
		decoded.tokenId.ShouldBe(1UL);
		((BigInteger)decoded.amount).ShouldBe(new BigInteger(100000000));

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(TransferFungibleArgsHex);
	}
}
