using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class TokenCallArgsTests
{
	private const string TransferFungibleArgsHex =
		"1111111111111111111111111111111111111111111111111111111111111111222222222222222222222222222222222222222222222222222222222222222201000000000000000800E1F50500000000";
	private const string MintFungibleArgsHex =
		"010000000000000011111111111111111111111111111111111111111111111111111111111111110800E1F50500000000";
	private const string TransferNonFungibleArgsHex =
		"1111111111111111111111111111111111111111111111111111111111111111222222222222222222222222222222222222222222222222222222222222222201000000000000000200000007000000000000000800000000000000";
	private const string BurnFungibleArgsHex =
		"010000000000000022222222222222222222222222222222222222222222222222222222222222220800E1F50500000000";
	private const string BurnNonFungibleArgsHex =
		"010000000000000022222222222222222222222222222222222222222222222222222222222222220200000007000000000000000800000000000000";
	private const string EmptySchemaHex =
		"0000000000";
	private const string SeriesInfoHex =
		"0300000009000000333333333333333333333333333333333333333333333333333333333333333302000000AABB" +
		EmptySchemaHex +
		EmptySchemaHex;
	private const string CreateTokenSeriesArgsHex =
		"0900000000000000" +
		SeriesInfoHex;
	private const string CreateMintedTokenSeriesArgsHex =
		"0900000000000000" +
		SeriesInfoHex +
		"4444444444444444444444444444444444444444444444444444444444444444" +
		"0200000002000000010200000000" +
		"010000000100000003";
	private const string UpdateTokenMetadataArgsHex =
		"0900000000000000" +
		"01000000016E16616C70686100";
	private const string UpdateSeriesMetadataArgsHex =
		"09000000000000000700000004000000DEADBEEF";
	private const string MintPhantasmaNonFungibleArgsHex =
		"0900000000000000" +
		"4444444444444444444444444444444444444444444444444444444444444444" +
		"02000000" +
		"082A0000000000000002000000AABB01000000CC" +
		"082B000000000000000000000002000000DDEE";
	private const string PhantasmaNftMintResultHex =
		"5555555555555555555555555555555555555555555555555555555555555555" +
		"7B00000000000000";

	[Fact]
	public void MintFungibleArgs_vector_roundtrip()
	{
		var args = new MintFungibleArgs
		{
			tokenId = 1,
			to = new Bytes32(Enumerable.Repeat((byte)0x11, 32).ToArray()),
			amount = new IntX(100000000)
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(MintFungibleArgsHex);

		var decoded = CarbonBlob.New<MintFungibleArgs>(MintFungibleArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(1UL);
		decoded.to.Equals(args.to).ShouldBeTrue();
		((BigInteger)decoded.amount).ShouldBe(new BigInteger(100000000));

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(MintFungibleArgsHex);
	}

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

	[Fact]
	public void TransferNonFungibleArgs_vector_roundtrip()
	{
		var args = new TransferNonFungibleArgs
		{
			to = new Bytes32(Enumerable.Repeat((byte)0x11, 32).ToArray()),
			from = new Bytes32(Enumerable.Repeat((byte)0x22, 32).ToArray()),
			tokenId = 1,
			instanceIds = [7, 8]
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(TransferNonFungibleArgsHex);

		var decoded = CarbonBlob.New<TransferNonFungibleArgs>(TransferNonFungibleArgsHex.FromHex()!);
		decoded.to.Equals(args.to).ShouldBeTrue();
		decoded.from.Equals(args.from).ShouldBeTrue();
		decoded.tokenId.ShouldBe(1UL);
		decoded.instanceIds.ShouldBe([7UL, 8UL]);

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(TransferNonFungibleArgsHex);
	}

	[Fact]
	public void BurnFungibleArgs_vector_roundtrip()
	{
		var args = new BurnFungibleArgs
		{
			tokenId = 1,
			from = new Bytes32(Enumerable.Repeat((byte)0x22, 32).ToArray()),
			amount = new IntX(100000000)
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(BurnFungibleArgsHex);

		var decoded = CarbonBlob.New<BurnFungibleArgs>(BurnFungibleArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(1UL);
		decoded.from.Equals(args.from).ShouldBeTrue();
		((BigInteger)decoded.amount).ShouldBe(new BigInteger(100000000));

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(BurnFungibleArgsHex);
	}

	[Fact]
	public void BurnNonFungibleArgs_vector_roundtrip()
	{
		var args = new BurnNonFungibleArgs
		{
			tokenId = 1,
			from = new Bytes32(Enumerable.Repeat((byte)0x22, 32).ToArray()),
			instanceIds = [7, 8]
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(BurnNonFungibleArgsHex);

		var decoded = CarbonBlob.New<BurnNonFungibleArgs>(BurnNonFungibleArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(1UL);
		decoded.from.Equals(args.from).ShouldBeTrue();
		decoded.instanceIds.ShouldBe([7UL, 8UL]);

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(BurnNonFungibleArgsHex);
	}

	[Fact]
	public void TokensConfig_vector_roundtrip()
	{
		var config = new TokensConfig
		{
			flags = TokensConfigFlags.RequireMetadata | TokensConfigFlags.AllowExplicitNftMetaIdMint
		};

		CarbonBlob.Serialize(config).ToHex().ShouldBe("11");

		var decoded = CarbonBlob.New<TokensConfig>("11".FromHex()!);
		decoded.flags.ShouldBe(TokensConfigFlags.RequireMetadata | TokensConfigFlags.AllowExplicitNftMetaIdMint);
	}

	[Fact]
	public void CreateTokenSeriesArgs_vector_roundtrip()
	{
		var args = new CreateTokenSeriesArgs
		{
			tokenId = 9,
			info = BuildSeriesInfo()
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(CreateTokenSeriesArgsHex);

		var decoded = CarbonBlob.New<CreateTokenSeriesArgs>(CreateTokenSeriesArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(9UL);
		AssertSeriesInfo(decoded.info);

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(CreateTokenSeriesArgsHex);
	}

	[Fact]
	public void CreateMintedTokenSeriesArgs_vector_roundtrip()
	{
		var args = new CreateMintedTokenSeriesArgs
		{
			tokenId = 9,
			info = BuildSeriesInfo(),
			address = RepeatedBytes32(0x44),
			roms = [new byte[] { 0x01, 0x02 }, Array.Empty<byte>()],
			rams = [new byte[] { 0x03 }]
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(CreateMintedTokenSeriesArgsHex);

		var decoded = CarbonBlob.New<CreateMintedTokenSeriesArgs>(CreateMintedTokenSeriesArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(9UL);
		AssertSeriesInfo(decoded.info);
		decoded.address.Equals(args.address).ShouldBeTrue();
		decoded.roms.Length.ShouldBe(2);
		decoded.roms[0].ShouldBe(new byte[] { 0x01, 0x02 });
		decoded.roms[1].ShouldBe(Array.Empty<byte>());
		decoded.rams.Length.ShouldBe(1);
		decoded.rams[0].ShouldBe(new byte[] { 0x03 });

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(CreateMintedTokenSeriesArgsHex);
	}

	[Fact]
	public void UpdateTokenMetadataArgs_vector_roundtrip()
	{
		var args = new UpdateTokenMetadataArgs
		{
			tokenId = 9,
			metadata = new VmDynamicStruct
			{
				fields =
				[
					new VmNamedDynamicVariable
					{
						name = new SmallString("n"),
						value = new VmDynamicVariable("alpha")
					}
				]
			}
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(UpdateTokenMetadataArgsHex);

		var decoded = CarbonBlob.New<UpdateTokenMetadataArgs>(UpdateTokenMetadataArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(9UL);
		decoded.metadata.fields.Length.ShouldBe(1);
		decoded.metadata.fields[0].name.data.ShouldBe("n");
		decoded.metadata.fields[0].value.GetString().ShouldBe("alpha");

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(UpdateTokenMetadataArgsHex);
	}

	[Fact]
	public void UpdateSeriesMetadataArgs_vector_roundtrip()
	{
		var args = new UpdateSeriesMetadataArgs
		{
			tokenId = 9,
			seriesId = 7,
			metadata = [0xDE, 0xAD, 0xBE, 0xEF]
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(UpdateSeriesMetadataArgsHex);

		var decoded = CarbonBlob.New<UpdateSeriesMetadataArgs>(UpdateSeriesMetadataArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(9UL);
		decoded.seriesId.ShouldBe(7U);
		decoded.metadata.ShouldBe(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(UpdateSeriesMetadataArgsHex);
	}

	[Fact]
	public void MintPhantasmaNonFungibleArgs_vector_roundtrip()
	{
		var args = new MintPhantasmaNonFungibleArgs
		{
			tokenId = 9,
			address = RepeatedBytes32(0x44),
			tokens =
			[
				new PhantasmaNftMintInfo
				{
					phantasmaSeriesId = new IntX(42),
					rom = [0xAA, 0xBB],
					ram = [0xCC]
				},
				new PhantasmaNftMintInfo
				{
					phantasmaSeriesId = new IntX(43),
					rom = Array.Empty<byte>(),
					ram = [0xDD, 0xEE]
				}
			]
		};

		CarbonBlob.Serialize(args).ToHex().ShouldBe(MintPhantasmaNonFungibleArgsHex);

		var decoded = CarbonBlob.New<MintPhantasmaNonFungibleArgs>(MintPhantasmaNonFungibleArgsHex.FromHex()!);
		decoded.tokenId.ShouldBe(9UL);
		decoded.address.Equals(args.address).ShouldBeTrue();
		decoded.tokens.Length.ShouldBe(2);
		((BigInteger)decoded.tokens[0].phantasmaSeriesId).ShouldBe(new BigInteger(42));
		decoded.tokens[0].rom.ShouldBe(new byte[] { 0xAA, 0xBB });
		decoded.tokens[0].ram.ShouldBe(new byte[] { 0xCC });
		((BigInteger)decoded.tokens[1].phantasmaSeriesId).ShouldBe(new BigInteger(43));
		decoded.tokens[1].rom.ShouldBe(Array.Empty<byte>());
		decoded.tokens[1].ram.ShouldBe(new byte[] { 0xDD, 0xEE });

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(MintPhantasmaNonFungibleArgsHex);
	}

	[Fact]
	public void PhantasmaNftMintResult_vector_roundtrip()
	{
		var result = new PhantasmaNftMintResult
		{
			phantasmaNftId = RepeatedBytes32(0x55),
			carbonInstanceId = 123
		};

		CarbonBlob.Serialize(result).ToHex().ShouldBe(PhantasmaNftMintResultHex);

		var decoded = CarbonBlob.New<PhantasmaNftMintResult>(PhantasmaNftMintResultHex.FromHex()!);
		decoded.phantasmaNftId.Equals(result.phantasmaNftId).ShouldBeTrue();
		decoded.carbonInstanceId.ShouldBe(123UL);

		CarbonBlob.Serialize(decoded).ToHex().ShouldBe(PhantasmaNftMintResultHex);
	}

	private static SeriesInfo BuildSeriesInfo()
	{
		return new SeriesInfo
		{
			maxMint = 3,
			maxSupply = 9,
			owner = RepeatedBytes32(0x33),
			metadata = [0xAA, 0xBB],
			rom = VmStructSchema.CreateEmpty(),
			ram = VmStructSchema.CreateEmpty()
		};
	}

	private static void AssertSeriesInfo(SeriesInfo seriesInfo)
	{
		seriesInfo.maxMint.ShouldBe(3U);
		seriesInfo.maxSupply.ShouldBe(9U);
		seriesInfo.owner.Equals(RepeatedBytes32(0x33)).ShouldBeTrue();
		seriesInfo.metadata.ShouldBe(new byte[] { 0xAA, 0xBB });
		seriesInfo.rom.fields.ShouldBe(Array.Empty<VmNamedVariableSchema>());
		seriesInfo.rom.flags.ShouldBe(VmStructSchema.Flags.None);
		seriesInfo.ram.fields.ShouldBe(Array.Empty<VmNamedVariableSchema>());
		seriesInfo.ram.flags.ShouldBe(VmStructSchema.Flags.None);
	}

	private static Bytes32 RepeatedBytes32(byte value)
	{
		return new Bytes32(Enumerable.Repeat(value, 32).ToArray());
	}
}
