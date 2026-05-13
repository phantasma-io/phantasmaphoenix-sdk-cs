using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class CarbonTxBuilderFixtureTests
{
	private const string SamplePngIconDataUri =
		"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";

	private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "carbon_tx_builder_vectors.tsv");

	public static IEnumerable<object[]> CarbonTxBuilderCases => ReadRows().Select(row => new object[] { row });

	[Theory]
	[MemberData(nameof(CarbonTxBuilderCases))]
	public void Carbon_transaction_builders_match_shared_golden_vectors(CarbonTxBuilderRow row)
	{
		// Each row must be rebuilt through public Carbon tx helper APIs so the
		// fixture proves builder behavior, not only byte-copy presence.
		(row.Source == "csharp_sdk" || row.Source == "go_sdk").ShouldBeTrue();
		BuildVector(row.CaseId).ToHex().ShouldBe(row.ExpectedHex);
		row.Notes.ShouldNotBeEmpty();
	}

	private static byte[] BuildVector(string caseId)
	{
		var sender = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
		var receiver = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");
		var senderPub = new Bytes32(sender.PublicKey);
		var receiverPub = new Bytes32(receiver.PublicKey);

		return caseId switch
		{
			"signed_transfer_fungible" => TxMsgSigner.Sign(new TxMsg
			{
				type = TxTypes.TransferFungible,
				expiry = 1759711416000,
				maxGas = 10000000,
				maxData = 1000,
				gasFrom = senderPub,
				payload = new SmallString("test-payload"),
				msg = new TxMsgTransferFungible
				{
					to = receiverPub,
					tokenId = 1,
					amount = 100000000
				}
			}, sender),
			"transfer_fungible_gas_payer" => CarbonBlob.Serialize(BaseTx(TxTypes.TransferFungible_GasPayer, senderPub, new TxMsgTransferFungible_GasPayer
			{
				to = receiverPub,
				from = senderPub,
				tokenId = 1,
				amount = 100000000
			})),
			"burn_fungible_gas_payer" => CarbonBlob.Serialize(BaseTx(TxTypes.BurnFungible_GasPayer, senderPub, new TxMsgBurnFungible_GasPayer
			{
				tokenId = 1,
				from = senderPub,
				amount = new IntX(100000000)
			})),
			"mint_fungible" => CarbonBlob.Serialize(BaseTx(TxTypes.MintFungible, senderPub, new TxMsgMintFungible
			{
				tokenId = 1,
				to = receiverPub,
				amount = new IntX(100000000)
			})),
			"create_token_nft" => CarbonBlob.Serialize(BuildCreateTokenTx(senderPub)),
			"create_token_series_u256_id" => CarbonBlob.Serialize(BuildCreateTokenSeriesTx(senderPub)),
			"mint_non_fungible_u256_nft_id" => CarbonBlob.Serialize(BuildMintNonFungibleTx(senderPub)),
			"mint_phantasma_nft_single_u255_series" => CarbonBlob.Serialize(BuildMintPhantasmaNonFungibleTx(senderPub, receiverPub)),
			_ => throw new InvalidOperationException($"unhandled Carbon tx builder vector {caseId}"),
		};
	}

	private static TxMsg BaseTx(TxTypes type, Bytes32 gasFrom, object msg)
	{
		return new TxMsg
		{
			type = type,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = gasFrom,
			payload = new SmallString("test-payload"),
			msg = msg
		};
	}

	private static TxMsg BuildCreateTokenTx(Bytes32 senderPub)
	{
		var tokenInfo = TokenInfoBuilder.Build(
			"MYNFT",
			new IntX(0),
			true,
			0,
			senderPub,
			TokenMetadataBuilder.BuildAndSerialize(new Dictionary<string, string>
			{
				["name"] = "My test token!",
				["icon"] = SamplePngIconDataUri,
				["url"] = "http://example.com",
				["description"] = "My test token description"
			}),
			TokenSchemasBuilder.BuildAndSerialize(null)
		);

		return CreateTokenTxHelper.BuildTx(
			tokenInfo,
			senderPub,
			new CreateTokenFeeOptions(10000, 10000000000, 10000000000, 10000),
			100000000,
			1759711416000);
	}

	private static TxMsg BuildCreateTokenSeriesTx(Bytes32 senderPub)
	{
		var seriesInfo = SeriesInfoBuilder.Build(
			(BigInteger.One << 256) - 1,
			0,
			0,
			senderPub
		);

		return CreateTokenSeriesTxHelper.BuildTx(
			ulong.MaxValue,
			seriesInfo,
			senderPub,
			new CreateSeriesFeeOptions(10000, 2500000000, 10000),
			100000000,
			1759711416000);
	}

	private static TxMsg BuildMintNonFungibleTx(Bytes32 senderPub)
	{
		var schemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var rom = NftRomBuilder.BuildAndSerialize(
			schemas.rom,
			(BigInteger.One << 256) - 1,
			NftMetadata(includeRawRom: true));

		return MintNonFungibleTxHelper.BuildTx(
			ulong.MaxValue,
			uint.MaxValue,
			senderPub,
			senderPub,
			rom,
			Array.Empty<byte>(),
			new MintNftFeeOptions(10000, 1000),
			100000000,
			1759711416000);
	}

	private static TxMsg BuildMintPhantasmaNonFungibleTx(Bytes32 senderPub, Bytes32 receiverPub)
	{
		var schemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var publicRom = PhantasmaNftRomBuilder.BuildAndSerialize(schemas.rom, NftMetadata(includeRawRom: false));

		return MintPhantasmaNonFungibleTxHelper.BuildTx(
			42,
			(BigInteger.One << 255) - 1,
			senderPub,
			receiverPub,
			publicRom,
			Array.Empty<byte>(),
			new MintNftFeeOptions(10000, 1000),
			123,
			1759711416000);
	}

	private static MetadataField[] NftMetadata(bool includeRawRom)
	{
		var fields = new List<MetadataField>
		{
			new("name", "My NFT #1"),
			new("description", "This is my first NFT!"),
			new("imageURL", "images-assets.nasa.gov/image/PIA13227/PIA13227~orig.jpg"),
			new("infoURL", "https://images.nasa.gov/details/PIA13227"),
			new("royalties", 10000000)
		};
		if (includeRawRom)
		{
			fields.Add(new MetadataField("rom", new byte[] { 0x01, 0x42 }));
		}
		return fields.ToArray();
	}

	private static IReadOnlyList<CarbonTxBuilderRow> ReadRows()
	{
		return File.ReadLines(FixturePath)
			.Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("case_id\t"))
			.Select(line =>
			{
				var parts = line.Split('\t');
				parts.Length.ShouldBe(4);
				return new CarbonTxBuilderRow(parts[0], parts[1], parts[2], parts[3]);
			})
			.ToArray();
	}

	public sealed record CarbonTxBuilderRow(string CaseId, string Source, string ExpectedHex, string Notes);
}
