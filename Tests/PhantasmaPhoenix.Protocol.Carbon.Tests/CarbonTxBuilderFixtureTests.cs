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
	// The vectors were generated with the SDK's former gas-model-v1 fee constants:
	// (10,000 base + product fee) x multiplier. The offers are part of the pinned bytes, so they are
	// reproduced here as the explicit limits the builders now take.
	private const long VectorExpiry = 1759711416000;
	private const ulong CreateTokenMaxGas = (10000UL + 10000000000UL + (10000000000UL >> 4)) * 10000; // 5-char symbol
	private const ulong CreateSeriesMaxGas = (10000UL + 2500000000UL) * 10000;
	private const ulong MintNftMaxGas = 10000UL * 1000;

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
			new TxLimits { MaxGas = CreateTokenMaxGas, MaxData = 100000000, Expiry = VectorExpiry });
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
			new TxLimits { MaxGas = CreateSeriesMaxGas, MaxData = 100000000, Expiry = VectorExpiry });
	}

	private static TxMsg BuildMintNonFungibleTx(Bytes32 senderPub)
	{
		var schemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var rom = NftRomBuilder.BuildAndSerialize(
			schemas.rom,
			(BigInteger.One << 256) - 1,
			NftMetadata(includeRawRom: true));

		// Built as the raw wire message: the SDK ships no builder for the native NFT mint, which the
		// chain's token config disables, but the wire type and its pinned bytes stay.
		return new TxMsg
		{
			type = TxTypes.MintNonFungible,
			expiry = VectorExpiry,
			maxGas = MintNftMaxGas,
			maxData = 100000000,
			gasFrom = senderPub,
			payload = SmallString.Empty,
			msg = new TxMsgMintNonFungible
			{
				tokenId = ulong.MaxValue,
				seriesId = uint.MaxValue,
				to = senderPub,
				rom = rom,
				ram = Array.Empty<byte>()
			}
		};
	}

	private static TxMsg BuildMintPhantasmaNonFungibleTx(Bytes32 senderPub, Bytes32 receiverPub)
	{
		var schemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var publicRom = PhantasmaNftRomBuilder.BuildAndSerialize(schemas.rom, NftMetadata(includeRawRom: false));

		return MintPhantasmaNonFungibleTxHelper.BuildTx(
			new PhantasmaNftMintParams
			{
				TokenId = 42,
				Sender = senderPub,
				To = receiverPub,
				Tokens = new[]
				{
					new PhantasmaNftMintInfo
					{
						phantasmaSeriesId = new IntX((BigInteger.One << 255) - 1),
						rom = publicRom,
						ram = Array.Empty<byte>()
					}
				}
			},
			new TxLimits { MaxGas = MintNftMaxGas, MaxData = 123, Expiry = VectorExpiry });
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
