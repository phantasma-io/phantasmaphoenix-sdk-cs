using System.IO;
using System.Linq;
using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class PhantasmaSafeMintTests
{
	private static readonly Bytes32 Sender = new(Enumerable.Repeat((byte)0x11, 32).ToArray());
	private static readonly Bytes32 Receiver = new(Enumerable.Repeat((byte)0x22, 32).ToArray());

	[Fact]
	public void PhantasmaNftRomBuilder_serializes_public_mint_payload_without_service_fields()
	{
		var tokenSchemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var rom = PhantasmaNftRomBuilder.BuildAndSerialize(tokenSchemas.rom, BuildMetadataFields());
		var publicSchema = PhantasmaNftRomBuilder.BuildPublicMintSchema(tokenSchemas.rom);

		var romStruct = VmDynamicStruct.New(publicSchema, rom);

		romStruct["name"].ShouldNotBeNull();
		romStruct["name"]!.Value.GetString().ShouldBe("My NFT #1");
		romStruct["description"].ShouldNotBeNull();
		romStruct["description"]!.Value.GetString().ShouldBe("This is my first NFT!");
		romStruct[StandardMeta.id].ShouldBeNull();
		romStruct["rom"].ShouldBeNull();
	}

	[Fact]
	public void MintPhantasmaNonFungibleTxHelper_builds_call_tx_with_deterministic_args()
	{
		var tokenSchemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var rom = PhantasmaNftRomBuilder.BuildAndSerialize(tokenSchemas.rom, BuildMetadataFields());

		var tx = MintPhantasmaNonFungibleTxHelper.BuildTx(
			new PhantasmaNftMintParams
			{
				TokenId = 42,
				Sender = Sender,
				To = Receiver,
				Tokens = new[]
				{
					new PhantasmaNftMintInfo { phantasmaSeriesId = new IntX(new BigInteger(777)), rom = rom, ram = Array.Empty<byte>() }
				}
			},
			new TxLimits { MaxData = 123, Expiry = 999 });

		tx.type.ShouldBe(TxTypes.Call);
		tx.gasFrom.Equals(Sender).ShouldBeTrue();
		tx.expiry.ShouldBe(999);
		tx.maxData.ShouldBe(123UL);
		var call = (TxMsgCall)tx.msg;
		call.moduleId.ShouldBe((uint)ModuleId.Token);
		call.methodId.ShouldBe((uint)TokenContract_Methods.MintPhantasmaNonFungible);

		var decoded = CarbonBlob.New<MintPhantasmaNonFungibleArgs>(call.args);
		decoded.tokenId.ShouldBe(42UL);
		decoded.address.Equals(Receiver).ShouldBeTrue();
		decoded.tokens.Length.ShouldBe(1);
		((BigInteger)decoded.tokens[0].phantasmaSeriesId).ShouldBe(new BigInteger(777));
		decoded.tokens[0].rom.ToHex().ShouldBe(rom.ToHex());
		decoded.tokens[0].ram.ShouldBeEmpty();
	}

	// The builder assembles the message only: without a MaxGas the offer stays zero, which marks the
	// message as not yet planned, and every instance the caller passed is carried whatever the limits
	// are. The fee is planned from these arguments afterwards, so a builder that dropped or merged an
	// instance would produce an offer for a cheaper transaction than the one being sent.
	[Fact]
	public void MintPhantasmaNonFungibleTxHelper_leaves_the_offer_unplanned_unless_limits_are_given()
	{
		var tokens = BuildMintInfos(3, new byte[] { 0xAA });

		var unplanned = MintPhantasmaNonFungibleTxHelper.BuildTx(new PhantasmaNftMintParams { TokenId = 42, Sender = Sender, To = Receiver, Tokens = tokens });
		unplanned.maxGas.ShouldBe(0UL);
		unplanned.maxData.ShouldBe(0UL);

		var limited = MintPhantasmaNonFungibleTxHelper.BuildTx(
			new PhantasmaNftMintParams { TokenId = 42, Sender = Sender, To = Receiver, Tokens = tokens },
			new TxLimits { MaxGas = 20_000, MaxData = 123, Expiry = 999 });
		limited.maxGas.ShouldBe(20_000UL);
		limited.maxData.ShouldBe(123UL);
		limited.expiry.ShouldBe(999);

		foreach (var built in new[] { unplanned, limited })
		{
			var decoded = CarbonBlob.New<MintPhantasmaNonFungibleArgs>(((TxMsgCall)built.msg).args);
			decoded.tokens.Length.ShouldBe(3);
			decoded.tokens.Select(t => (BigInteger)t.phantasmaSeriesId).ShouldBe(new BigInteger[] { 1, 2, 3 });
		}

		Should.Throw<ArgumentException>(() => MintPhantasmaNonFungibleTxHelper.BuildTx(new PhantasmaNftMintParams { TokenId = 42, Sender = Sender, To = Receiver }));
	}

	[Fact]
	public void MintPhantasmaNonFungibleTxHelper_parse_result_preserves_both_ids()
	{
		var lowIdBytes = new byte[32];
		lowIdBytes[0] = 0x7B;
		var highIdBytes = new byte[32];
		highIdBytes[0] = 0x2A;
		highIdBytes[31] = 0x80;

		var expected = new[]
		{
			new PhantasmaNftMintResult
			{
				phantasmaNftId = new Bytes32(lowIdBytes),
				carbonInstanceId = 7
			},
			new PhantasmaNftMintResult
			{
				phantasmaNftId = new Bytes32(highIdBytes),
				carbonInstanceId = 8
			}
		};

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.WriteArray(expected);

		var parsed = MintPhantasmaNonFungibleTxHelper.ParseResult(stream.ToArray().ToHex());
		parsed.Length.ShouldBe(2);
		parsed[0].phantasmaNftId.Equals(new Bytes32(lowIdBytes)).ShouldBeTrue();
		parsed[0].carbonInstanceId.ShouldBe(7UL);
		parsed[1].phantasmaNftId.Equals(new Bytes32(highIdBytes)).ShouldBeTrue();
		parsed[1].carbonInstanceId.ShouldBe(8UL);
	}

	private static MetadataField[] BuildMetadataFields() =>
	[
		new MetadataField("name", "My NFT #1"),
		new MetadataField("description", "This is my first NFT!"),
		new MetadataField("imageURL", "images-assets.nasa.gov/image/PIA13227/PIA13227~orig.jpg"),
		new MetadataField("infoURL", "https://images.nasa.gov/details/PIA13227"),
		new MetadataField("royalties", 10000000)
	];

	private static PhantasmaNftMintInfo[] BuildMintInfos(int count, byte[] rom) =>
		Enumerable.Range(1, count)
			.Select(i => new PhantasmaNftMintInfo
			{
				phantasmaSeriesId = new IntX(new BigInteger(i)),
				rom = rom,
				ram = Array.Empty<byte>()
			})
			.ToArray();
}
