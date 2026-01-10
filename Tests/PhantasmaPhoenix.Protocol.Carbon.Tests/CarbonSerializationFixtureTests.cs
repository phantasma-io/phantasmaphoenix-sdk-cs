using System.Globalization;
using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class CarbonSerializationFixtureTests
{
	private const string SamplePngIconDataUri =
		"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";

	private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "carbon_vectors.tsv");

	public sealed record Row(string Kind, string Value, string Hex, string? DecOrig, string? DecBack);

	public static IEnumerable<object[]> Rows => LoadRows().Select(row => new object[] { row });

	[Theory]
	[MemberData(nameof(Rows))]
	public void Encode_fixtures_match(Row row)
	{
		var bytes = EncodeRow(row);
		bytes.ToHex().ShouldBe(row.Hex.ToUpperInvariant());
	}

	[Theory]
	[MemberData(nameof(Rows))]
	public void Decode_fixtures_match(Row row)
	{
		var decoded = DecodeRow(row);

		switch (row.Kind)
		{
			case "U8":
				((byte)decoded!).ShouldBe(ParseByte(row.Value));
				break;
			case "I16":
				((short)decoded!).ShouldBe(ParseInt16(row.Value));
				break;
			case "I32":
				((int)decoded!).ShouldBe(ParseInt32(row.Value));
				break;
			case "U32":
				((uint)decoded!).ShouldBe(ParseUInt32(row.Value));
				break;
			case "I64":
				((long)decoded!).ShouldBe(ParseInt64(row.Value));
				break;
			case "U64":
				((ulong)decoded!).ShouldBe(ParseUInt64(row.Value));
				break;
			case "BI":
				((BigInteger)decoded!).ShouldBe(ParseExpectedBigInt(row));
				break;
			case "INTX":
				((IntX)decoded!).ToString().ShouldBe(ParseExpectedIntX(row).ToString());
				break;
			case "FIX16":
				((Bytes16)decoded!).bytes.ToHex().ShouldBe(row.Value.ToUpperInvariant());
				break;
			case "FIX32":
				((Bytes32)decoded!).bytes.ToHex().ShouldBe(row.Value.ToUpperInvariant());
				break;
			case "FIX64":
				((Bytes64)decoded!).bytes.ToHex().ShouldBe(row.Value.ToUpperInvariant());
				break;
			case "SZ":
				decoded.ShouldBe(row.Value);
				break;
			case "ARRSZ":
				{
					var expected = ParseCsv(row.Value);
					var actual = (string[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARR8":
				{
					var expected = ParseCsv(row.Value).Select(ParseSByte).ToArray();
					var actual = (sbyte[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARR16":
				{
					var expected = ParseCsv(row.Value).Select(ParseInt16).ToArray();
					var actual = (short[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARR32":
				{
					var expected = ParseCsv(row.Value).Select(ParseInt32).ToArray();
					var actual = (int[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARR64":
				{
					var expected = ParseCsv(row.Value).Select(ParseInt64).ToArray();
					var actual = (long[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARRU64":
				{
					var expected = ParseCsv(row.Value).Select(ParseUInt64).ToArray();
					var actual = (ulong[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARRBYTES-1D":
				((byte[])decoded!).ToHex().ShouldBe(row.Value.ToUpperInvariant());
				break;
			case "ARRBYTES-2D":
				{
					var expected = ParseArrBytes2D(row.Value).Select(x => x.ToHex()).ToArray();
					var actual = ((byte[][])decoded!).Select(x => x.ToHex()).ToArray();
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "ARRBI":
				{
					var expected = ParseCsv(row.Value).Select(ParseBigInt).ToArray();
					var actual = (BigInteger[])decoded!;
					actual.SequenceEqual(expected).ShouldBeTrue();
					break;
				}
			case "VMSTRUCT01":
				{
					var schemas = (TokenSchemas)decoded!;
					ExpectStandardTokenSchemas(schemas);
					ExpectReencoded(schemas, row.Hex);
					break;
				}
			case "VMSTRUCT02":
				{
					var metadata = (VmDynamicStruct)decoded!;
					metadata.fields.Length.ShouldBe(4);
					ExpectStructString(metadata, "name", "My test token!");
					ExpectStructString(metadata, "icon", SamplePngIconDataUri);
					ExpectStructString(metadata, "url", "http://example.com");
					ExpectStructString(metadata, "description", "My test token description");
					ExpectReencoded(metadata, row.Hex);
					break;
				}
			case "TX1":
				{
					var tx = (TxMsg)decoded!;
					tx.type.ShouldBe(TxTypes.TransferFungible);
					tx.expiry.ShouldBe(1759711416000);
					tx.maxGas.ShouldBe(10000000UL);
					tx.maxData.ShouldBe(1000UL);
					tx.gasFrom.Equals(Bytes32.Empty).ShouldBeTrue();
					tx.payload.data.ShouldBe("test-payload");

					var msg = (TxMsgTransferFungible)tx.msg;
					msg.tokenId.ShouldBe(1UL);
					msg.amount.ShouldBe(100000000UL);
					msg.to.Equals(Bytes32.Empty).ShouldBeTrue();

					ExpectReencoded(tx, row.Hex);
					break;
				}
			case "TX2":
				{
					var txSender = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
					var txReceiver = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");
					var senderPubKey = new Bytes32(txSender.PublicKey);
					var receiverPubKey = new Bytes32(txReceiver.PublicKey);

					var signed = (SignedTxMsg)decoded!;
					var tx = signed.msg;
					tx.type.ShouldBe(TxTypes.TransferFungible);
					tx.expiry.ShouldBe(1759711416000);
					tx.maxGas.ShouldBe(10000000UL);
					tx.maxData.ShouldBe(1000UL);
					tx.gasFrom.Equals(senderPubKey).ShouldBeTrue();
					tx.payload.data.ShouldBe("test-payload");

					var msg = (TxMsgTransferFungible)tx.msg;
					msg.tokenId.ShouldBe(1UL);
					msg.amount.ShouldBe(100000000UL);
					msg.to.Equals(receiverPubKey).ShouldBeTrue();

					signed.witnesses.Length.ShouldBe(1);
					signed.witnesses[0].address.Equals(senderPubKey).ShouldBeTrue();
					signed.witnesses[0].signature.bytes.Length.ShouldBe(64);

					ExpectReencoded(signed, row.Hex);
					break;
				}
			case "TX-CREATE-TOKEN":
				{
					var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
					var symbol = "MYNFT";
					ulong maxData = 100000000;
					ulong gasFeeBase = 10000;
					ulong gasFeeCreateTokenBase = 10000000000;
					ulong gasFeeCreateTokenSymbol = 10000000000;
					ulong feeMultiplier = 10000;

					var txSender = PhantasmaKeys.FromWIF(wif);
					var txSenderPubKey = new Bytes32(txSender.PublicKey);

					var feeOptions = new CreateTokenFeeOptions(
						gasFeeBase,
						gasFeeCreateTokenBase,
						gasFeeCreateTokenSymbol,
						feeMultiplier
					);

					var tx = (TxMsg)decoded!;
					tx.type.ShouldBe(TxTypes.Call);
					tx.expiry.ShouldBe(1759711416000);
					tx.maxData.ShouldBe(maxData);
					tx.gasFrom.Equals(txSenderPubKey).ShouldBeTrue();
					tx.payload.data.ShouldBe(string.Empty);

					var call = (TxMsgCall)tx.msg;
					call.moduleId.ShouldBe((uint)ModuleId.Token);
					call.methodId.ShouldBe((uint)TokenContract_Methods.CreateToken);
					call.args.Length.ShouldBeGreaterThan(0);

					var tokenInfo = CarbonBlob.New<TokenInfo>(call.args);
					tokenInfo.symbol.data.ShouldBe(symbol);
					tokenInfo.decimals.ShouldBe(0u);
					tokenInfo.flags.ShouldBe(TokenFlags.NonFungible);
					tokenInfo.owner.Equals(txSenderPubKey).ShouldBeTrue();
					tokenInfo.maxSupply.IsZero.ShouldBeTrue();

					var metadata = CarbonBlob.New<VmDynamicStruct>(tokenInfo.metadata);
					metadata.fields.Length.ShouldBe(4);
					ExpectStructString(metadata, "name", "My test token!");
					ExpectStructString(metadata, "icon", SamplePngIconDataUri);
					ExpectStructString(metadata, "url", "http://example.com");
					ExpectStructString(metadata, "description", "My test token description");

					tokenInfo.tokenSchemas.ShouldNotBeNull();
					var schemas = CarbonBlob.New<TokenSchemas>(tokenInfo.tokenSchemas);
					ExpectStandardTokenSchemas(schemas);

					var expectedMaxGas = feeOptions.CalculateMaxGas(tokenInfo.symbol);
					tx.maxGas.ShouldBe(expectedMaxGas);

					ExpectReencoded(tx, row.Hex);
					break;
				}
			case "TX-CREATE-TOKEN-SERIES":
				{
					var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
					ulong tokenId = ulong.MaxValue;
					ulong maxData = 100000000;
					ulong gasFeeBase = 10000;
					ulong gasFeeCreateTokenSeries = 2500000000;
					ulong feeMultiplier = 10000;

					var txSender = PhantasmaKeys.FromWIF(wif);
					var txSenderPubKey = new Bytes32(txSender.PublicKey);
					var newPhantasmaSeriesId = (BigInteger.One << 256) - 1;

					var feeOptions = new CreateSeriesFeeOptions(
						gasFeeBase,
						gasFeeCreateTokenSeries,
						feeMultiplier
					);

					var tx = (TxMsg)decoded!;
					tx.type.ShouldBe(TxTypes.Call);
					tx.expiry.ShouldBe(1759711416000);
					tx.maxData.ShouldBe(maxData);
					tx.gasFrom.Equals(txSenderPubKey).ShouldBeTrue();
					tx.payload.data.ShouldBe(string.Empty);

					var call = (TxMsgCall)tx.msg;
					call.moduleId.ShouldBe((uint)ModuleId.Token);
					call.methodId.ShouldBe((uint)TokenContract_Methods.CreateTokenSeries);

					using var argsStream = new MemoryStream(call.args);
					using var argsReader = new BinaryReader(argsStream);
					argsReader.Read8(out ulong decodedTokenId);
					decodedTokenId.ShouldBe(tokenId);
					var seriesInfo = argsReader.Read<SeriesInfo>();

					seriesInfo.maxMint.ShouldBe(0u);
					seriesInfo.maxSupply.ShouldBe(0u);
					seriesInfo.owner.Equals(txSenderPubKey).ShouldBeTrue();
					seriesInfo.rom.fields.Length.ShouldBe(0);
					seriesInfo.ram.fields.Length.ShouldBe(0);

					var schemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
					var seriesMeta = VmDynamicStruct.New(schemas.seriesMetadata, seriesInfo.metadata);
					seriesMeta.fields.Length.ShouldBe(3);
					ExpectStructInt256(seriesMeta, StandardMeta.id.data, ToSignedInt256(newPhantasmaSeriesId));
					ExpectStructInt8(seriesMeta, "mode", 0);
					ExpectStructBytes(seriesMeta, "rom", Array.Empty<byte>());

					var expectedMaxGas = feeOptions.CalculateMaxGas();
					tx.maxGas.ShouldBe(expectedMaxGas);

					ExpectReencoded(tx, row.Hex);
					break;
				}
			case "TX-MINT-NON-FUNGIBLE":
				{
					var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
					ulong carbonTokenId = ulong.MaxValue;
					uint carbonSeriesId = uint.MaxValue;
					ulong maxData = 100000000;
					ulong gasFeeBase = 10000;
					ulong feeMultiplier = 1000;

					var txSender = PhantasmaKeys.FromWIF(wif);
					var txSenderPubKey = new Bytes32(txSender.PublicKey);

					BigInteger phantasmaId = (BigInteger.One << 256) - 1;
					byte[] phantasmaRomData = { 0x01, 0x42 };

					var feeOptions = new MintNftFeeOptions(gasFeeBase, feeMultiplier);

					var tx = (TxMsg)decoded!;
					tx.type.ShouldBe(TxTypes.MintNonFungible);
					tx.expiry.ShouldBe(1759711416000);
					tx.maxData.ShouldBe(maxData);
					tx.gasFrom.Equals(txSenderPubKey).ShouldBeTrue();
					tx.payload.data.ShouldBe(string.Empty);

					var mint = (TxMsgMintNonFungible)tx.msg;
					mint.tokenId.ShouldBe(carbonTokenId);
					mint.seriesId.ShouldBe(carbonSeriesId);
					mint.to.Equals(txSenderPubKey).ShouldBeTrue();
					mint.ram.Length.ShouldBe(0);

					var schemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
					var romStruct = VmDynamicStruct.New(schemas.rom, mint.rom);
					ExpectStructInt256(romStruct, StandardMeta.id.data, ToSignedInt256(phantasmaId));
					ExpectStructBytes(romStruct, "rom", phantasmaRomData);
					ExpectStructString(romStruct, "name", "My NFT #1");
					ExpectStructString(romStruct, "description", "This is my first NFT!");
					ExpectStructString(romStruct, "imageURL", "images-assets.nasa.gov/image/PIA13227/PIA13227~orig.jpg");
					ExpectStructString(romStruct, "infoURL", "https://images.nasa.gov/details/PIA13227");
					ExpectStructInt32(romStruct, "royalties", 10000000);

					var expectedMaxGas = feeOptions.CalculateMaxGas();
					tx.maxGas.ShouldBe(expectedMaxGas);

					ExpectReencoded(tx, row.Hex);
					break;
				}
			default:
				throw new InvalidOperationException($"Unhandled kind: {row.Kind}");
		}
	}

	private static IReadOnlyList<Row> LoadRows()
	{
		if (!File.Exists(FixturePath))
		{
			throw new FileNotFoundException($"Missing fixture: {FixturePath}");
		}

		var lines = File.ReadAllLines(FixturePath)
			.Select(line => line.TrimEnd())
			.Where(line => !string.IsNullOrWhiteSpace(line))
			.ToArray();

		if (lines.Length > 0)
		{
			lines[0] = lines[0].TrimStart('\uFEFF');
		}

		var rows = new List<Row>(lines.Length);
		foreach (var line in lines)
		{
			var parts = line.Split('\t');
			if (parts.Length == 3)
			{
				rows.Add(new Row(parts[0], parts[1], parts[2], null, null));
				continue;
			}

			if (parts.Length == 5 && (parts[0] == "BI" || parts[0] == "INTX"))
			{
				rows.Add(new Row(parts[0], parts[1], parts[2], parts[3], parts[4]));
				continue;
			}

			throw new InvalidOperationException($"Bad TSV line: {line}");
		}

		return rows;
	}

	private static byte[] EncodeRow(Row row)
	{
		switch (row.Kind)
		{
			case "VMSTRUCT01":
				return BuildVmStruct1Bytes();
			case "VMSTRUCT02":
				return BuildVmStruct2Bytes();
			case "TX1":
				return CarbonBlob.Serialize(BuildTx1());
			case "TX2":
				return BuildSignedTx2Bytes();
			case "TX-CREATE-TOKEN":
				return CarbonBlob.Serialize(BuildCreateTokenTx());
			case "TX-CREATE-TOKEN-SERIES":
				return CarbonBlob.Serialize(BuildCreateTokenSeriesTx());
			case "TX-MINT-NON-FUNGIBLE":
				return CarbonBlob.Serialize(BuildMintNonFungibleTx());
		}

		using var ms = new MemoryStream();
		using var w = new BinaryWriter(ms);

		switch (row.Kind)
		{
			case "U8":
				w.Write1(ParseByte(row.Value));
				break;
			case "I16":
				w.Write2(ParseInt16(row.Value));
				break;
			case "I32":
				w.Write4(ParseInt32(row.Value));
				break;
			case "U32":
				w.Write4(ParseUInt32(row.Value));
				break;
			case "I64":
				w.Write8(ParseInt64(row.Value));
				break;
			case "U64":
				w.Write8(ParseUInt64(row.Value));
				break;
			case "FIX16":
				w.Write16(ParseHex(row.Value));
				break;
			case "FIX32":
				w.Write32(ParseHex(row.Value));
				break;
			case "FIX64":
				w.Write64(ParseHex(row.Value));
				break;
			case "SZ":
				w.WriteSz(row.Value);
				break;
			case "ARRSZ":
				w.WriteArraySz(ParseCsv(row.Value));
				break;
			case "ARR8":
				w.WriteArray8(ParseCsv(row.Value).Select(ParseSByte).ToArray());
				break;
			case "ARR16":
				w.WriteArray16(ParseCsv(row.Value).Select(ParseInt16).ToArray());
				break;
			case "ARR32":
				w.WriteArray32(ParseCsv(row.Value).Select(ParseInt32).ToArray());
				break;
			case "ARR64":
				w.WriteArray64(ParseCsv(row.Value).Select(ParseInt64).ToArray());
				break;
			case "ARRU64":
				w.WriteArray64(ParseCsv(row.Value).Select(ParseUInt64).ToArray());
				break;
			case "ARRBYTES-1D":
				w.WriteArray(ParseHex(row.Value));
				break;
			case "ARRBYTES-2D":
				w.WriteArray(ParseArrBytes2D(row.Value));
				break;
			case "BI":
				w.WriteBigInt(ParseBigInt(row.Value));
				break;
			case "INTX":
				ParseIntX(row.Value).Write(w);
				break;
			case "ARRBI":
				w.WriteArrayBigInt(ParseCsv(row.Value).Select(ParseBigInt).ToArray());
				break;
			default:
				throw new InvalidOperationException($"Unhandled kind: {row.Kind}");
		}

		return ms.ToArray();
	}

	private static object? DecodeRow(Row row)
	{
		var bytes = row.Hex.FromHex() ?? Array.Empty<byte>();
		using var ms = new MemoryStream(bytes);
		using var r = new BinaryReader(ms);

		switch (row.Kind)
		{
			case "U8":
				return r.Read1();
			case "I16":
				return r.Read2();
			case "I32":
				return r.Read4();
			case "U32":
				r.Read4(out uint u32);
				return u32;
			case "I64":
				r.Read8(out long i64);
				return i64;
			case "U64":
				r.Read8(out ulong u64);
				return u64;
			case "FIX16":
				return r.Read16();
			case "FIX32":
				return r.Read32();
			case "FIX64":
				return r.Read64();
			case "SZ":
				return r.ReadSz();
			case "ARRSZ":
				return r.ReadArraySz();
			case "ARR8":
				return r.ReadArray8();
			case "ARR16":
				return r.ReadArray16();
			case "ARR32":
				return r.ReadArray32();
			case "ARR64":
				return r.ReadArray64();
			case "ARRU64":
				r.ReadArray64(out ulong[] data);
				return data;
			case "ARRBYTES-1D":
				return r.ReadArray();
			case "ARRBYTES-2D":
				return r.ReadArrayArray();
			case "BI":
				return r.ReadBigInt();
			case "INTX":
				return r.Read<IntX>();
			case "ARRBI":
				return r.ReadArrayBigInt();
			case "VMSTRUCT01":
				return r.Read<TokenSchemas>();
			case "VMSTRUCT02":
				return r.Read<VmDynamicStruct>();
			case "TX1":
				return r.Read<TxMsg>();
			case "TX2":
				return r.Read<SignedTxMsg>();
			case "TX-CREATE-TOKEN":
				return r.Read<TxMsg>();
			case "TX-CREATE-TOKEN-SERIES":
				return r.Read<TxMsg>();
			case "TX-MINT-NON-FUNGIBLE":
				return r.Read<TxMsg>();
			default:
				throw new InvalidOperationException($"Unhandled kind: {row.Kind}");
		}
	}

	private static byte[] BuildVmStruct1Bytes() => TokenSchemasBuilder.BuildAndSerialize(null);

	private static byte[] BuildVmStruct2Bytes()
	{
		var fields = new Dictionary<string, string>
		{
			["name"] = "My test token!",
			["icon"] = SamplePngIconDataUri,
			["url"] = "http://example.com",
			["description"] = "My test token description"
		};

		var metadataFields = fields.Select(f => new VmNamedDynamicVariable
		{
			name = new SmallString(f.Key),
			value = new VmDynamicVariable(f.Value)
		}).ToArray();

		var metadata = new VmDynamicStruct { fields = metadataFields };

		using MemoryStream buffer = new();
		using BinaryWriter writer = new(buffer);
		writer.Write(metadata);
		return buffer.ToArray();
	}

	private static TxMsg BuildTx1() => new()
	{
		type = TxTypes.TransferFungible,
		expiry = 1759711416000,
		maxGas = 10000000,
		maxData = 1000,
		gasFrom = new Bytes32(),
		payload = new SmallString("test-payload"),
		msg = new TxMsgTransferFungible
		{
			to = new Bytes32(),
			tokenId = 1,
			amount = 100000000
		}
	};

	private static byte[] BuildSignedTx2Bytes()
	{
		var txSender = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
		var txReceiver = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");

		var tx = new TxMsg
		{
			type = TxTypes.TransferFungible,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = new Bytes32(txSender.PublicKey),
			payload = new SmallString("test-payload"),
			msg = new TxMsgTransferFungible
			{
				to = new Bytes32(txReceiver.PublicKey),
				tokenId = 1,
				amount = 100000000
			}
		};

		var sig = new Bytes64(Ed25519.Sign(CarbonBlob.Serialize(tx), txSender.PrivateKey));
		var witness = new Witness
		{
			address = new Bytes32(txSender.PublicKey),
			signature = sig
		};

		var signed = new SignedTxMsg
		{
			msg = tx,
			witnesses = new[] { witness }
		};

		return CarbonBlob.Serialize(signed);
	}

	private static TxMsg BuildCreateTokenTx()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		var symbol = "MYNFT";
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong gasFeeCreateTokenBase = 10000000000;
		ulong gasFeeCreateTokenSymbol = 10000000000;
		ulong feeMultiplier = 10000;

		var txSender = PhantasmaKeys.FromWIF(wif);
		var txSenderPubKey = new Bytes32(txSender.PublicKey);

		var fields = new Dictionary<string, string>
		{
			["name"] = "My test token!",
			["icon"] = SamplePngIconDataUri,
			["url"] = "http://example.com",
			["description"] = "My test token description"
		};

		var tokenInfo = TokenInfoBuilder.Build(
			symbol,
			new IntX(0),
			true,
			0,
			txSenderPubKey,
			TokenMetadataBuilder.BuildAndSerialize(fields),
			TokenSchemasBuilder.BuildAndSerialize(null)
		);

		var feeOptions = new CreateTokenFeeOptions(
			gasFeeBase,
			gasFeeCreateTokenBase,
			gasFeeCreateTokenSymbol,
			feeMultiplier
		);

		return CreateTokenTxHelper.BuildTx(
			tokenInfo,
			txSenderPubKey,
			feeOptions,
			maxData,
			1759711416000
		);
	}

	private static TxMsg BuildCreateTokenSeriesTx()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		ulong tokenId = ulong.MaxValue;
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong gasFeeCreateTokenSeries = 2500000000;
		ulong feeMultiplier = 10000;

		var txSender = PhantasmaKeys.FromWIF(wif);
		var txSenderPubKey = new Bytes32(txSender.PublicKey);

		var newPhantasmaSeriesId = (BigInteger.One << 256) - 1;

		var seriesInfo = SeriesInfoBuilder.Build(
			newPhantasmaSeriesId,
			0,
			0,
			txSenderPubKey
		);

		var feeOptions = new CreateSeriesFeeOptions(
			gasFeeBase,
			gasFeeCreateTokenSeries,
			feeMultiplier
		);

		return CreateTokenSeriesTxHelper.BuildTx(
			tokenId,
			seriesInfo,
			txSenderPubKey,
			feeOptions,
			maxData,
			1759711416000
		);
	}

	private static TxMsg BuildMintNonFungibleTx()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		ulong carbonTokenId = ulong.MaxValue;
		uint carbonSeriesId = uint.MaxValue;
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong feeMultiplier = 1000;

		var txSender = PhantasmaKeys.FromWIF(wif);

		BigInteger phantasmaId = (BigInteger.One << 256) - 1;
		byte[] phantasmaRomData = { 0x01, 0x42 };

		var tokenSchemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var metadataFields = new[]
		{
			new MetadataField("name", "My NFT #1"),
			new MetadataField("description", "This is my first NFT!"),
			new MetadataField("imageURL", "images-assets.nasa.gov/image/PIA13227/PIA13227~orig.jpg"),
			new MetadataField("infoURL", "https://images.nasa.gov/details/PIA13227"),
			new MetadataField("royalties", 10000000),
			new MetadataField("rom", phantasmaRomData)
		};

		var rom = NftRomBuilder.BuildAndSerialize(tokenSchemas.rom, phantasmaId, metadataFields);

		var feeOptions = new MintNftFeeOptions(gasFeeBase, feeMultiplier);

		return MintNonFungibleTxHelper.BuildTx(
			carbonTokenId,
			carbonSeriesId,
			new Bytes32(txSender.PublicKey),
			new Bytes32(txSender.PublicKey),
			rom,
			Array.Empty<byte>(),
			feeOptions,
			maxData,
			1759711416000
		);
	}

	private static void ExpectReencoded<T>(T value, string expectedHex) where T : ICarbonBlob, new()
	{
		CarbonBlob.Serialize(value).ToHex().ShouldBe(expectedHex.ToUpperInvariant());
	}

	private static VmNamedDynamicVariable GetStructField(VmDynamicStruct structure, string name)
	{
		foreach (var field in structure.fields)
		{
			if (field.name.data == name)
			{
				return field;
			}
		}

		throw new InvalidOperationException($"Missing struct field '{name}'");
	}

	private static void ExpectStructString(VmDynamicStruct structure, string name, string expected)
	{
		var field = GetStructField(structure, name);
		field.value.type.ShouldBe(VmType.String);
		field.value.GetString().ShouldBe(expected);
	}

	private static void ExpectStructBytes(VmDynamicStruct structure, string name, byte[] expected)
	{
		var field = GetStructField(structure, name);
		field.value.type.ShouldBe(VmType.Bytes);
		field.value.GetBytes().ToHex().ShouldBe(expected.ToHex());
	}

	private static void ExpectStructInt256(VmDynamicStruct structure, string name, BigInteger expected)
	{
		var field = GetStructField(structure, name);
		field.value.type.ShouldBe(VmType.Int256);
		field.value.GetInt256().ShouldBe(expected);
	}

	private static void ExpectStructInt8(VmDynamicStruct structure, string name, sbyte expected)
	{
		var field = GetStructField(structure, name);
		field.value.type.ShouldBe(VmType.Int8);
		field.value.GetInt8().ShouldBe(expected);
	}

	private static void ExpectStructInt32(VmDynamicStruct structure, string name, int expected)
	{
		var field = GetStructField(structure, name);
		field.value.type.ShouldBe(VmType.Int32);
		field.value.GetInt32().ShouldBe(expected);
	}

	private static void ExpectSchemaMatches(VmStructSchema actual, VmStructSchema expected)
	{
		actual.fields.Select(f => f.name.data).ToArray().ShouldBe(expected.fields.Select(f => f.name.data).ToArray());
		actual.fields.Select(f => f.schema.type).ToArray().ShouldBe(expected.fields.Select(f => f.schema.type).ToArray());
		actual.flags.ShouldBe(expected.flags);
	}

	private static void ExpectStandardTokenSchemas(TokenSchemas schemas)
	{
		var expected = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		ExpectSchemaMatches(schemas.seriesMetadata, expected.seriesMetadata);
		ExpectSchemaMatches(schemas.rom, expected.rom);
		ExpectSchemaMatches(schemas.ram, expected.ram);
	}

	// VM Int256 is stored as two's complement in 256-bit space; normalize unsigned values for comparisons.
	private static BigInteger ToSignedInt256(BigInteger value)
	{
		var mask = (BigInteger.One << 256) - 1;
		var signBit = BigInteger.One << 255;
		var normalized = value & mask;
		return (normalized & signBit) == 0 ? normalized : normalized - (BigInteger.One << 256);
	}

	private static byte[] ParseHex(string value) => value.FromHex() ?? Array.Empty<byte>();

	private static byte ParseByte(string value) => (byte)ParseBigInt(value);

	private static sbyte ParseSByte(string value) => (sbyte)ParseBigInt(value);

	private static short ParseInt16(string value) => (short)ParseBigInt(value);

	private static int ParseInt32(string value) => (int)ParseBigInt(value);

	private static long ParseInt64(string value) => (long)ParseBigInt(value);

	private static uint ParseUInt32(string value) => (uint)ParseBigInt(value);

	private static ulong ParseUInt64(string value) => (ulong)ParseBigInt(value);

	private static BigInteger ParseBigInt(string value)
	{
		var trimmed = value.Trim();
		if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return BigInteger.Parse(trimmed[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		}

		return BigInteger.Parse(trimmed, CultureInfo.InvariantCulture);
	}

	private static IntX ParseIntX(string value)
	{
		var big = ParseBigInt(value);
		if (big >= long.MinValue && big <= long.MaxValue)
		{
			return new IntX((long)big);
		}
		return new IntX(big);
	}

	private static BigInteger ParseExpectedBigInt(Row row)
	{
		if (!string.IsNullOrWhiteSpace(row.DecBack))
		{
			return ParseBigInt(row.DecBack!);
		}

		return ParseBigInt(row.Value);
	}

	private static IntX ParseExpectedIntX(Row row)
	{
		if (!string.IsNullOrWhiteSpace(row.DecBack))
		{
			return ParseIntX(row.DecBack!);
		}

		return ParseIntX(row.Value);
	}

	private static string[] ParseCsv(string value)
		=> value.Length == 0 ? Array.Empty<string>() : value.Split(',');

	private static byte[][] ParseArrBytes2D(string value)
	{
		// Expected format: "[[01,02],[03,04,05]]"
		var trimmed = value.Trim();
		if (!trimmed.StartsWith("[[", StringComparison.Ordinal) || !trimmed.EndsWith("]]", StringComparison.Ordinal))
		{
			return Array.Empty<byte[]>();
		}

		var inner = trimmed.Substring(2, trimmed.Length - 4);
		var parts = inner.Split("],[", StringSplitOptions.None);

		return parts
			.Select(segment => segment.Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(entry => entry.Trim())
				.Aggregate(string.Empty, (acc, next) => acc + next))
			.Select(ParseHex)
			.ToArray();
	}
}
