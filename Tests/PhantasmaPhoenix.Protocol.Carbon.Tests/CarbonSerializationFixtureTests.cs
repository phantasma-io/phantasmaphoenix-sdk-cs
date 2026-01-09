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
				BuildVmStruct1Bytes().ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
			case "VMSTRUCT02":
				BuildVmStruct2Bytes().ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
			case "TX1":
				CarbonBlob.Serialize(BuildTx1()).ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
			case "TX2":
				BuildSignedTx2Bytes().ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
			case "TX-CREATE-TOKEN":
				CarbonBlob.Serialize(BuildCreateTokenTx()).ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
			case "TX-CREATE-TOKEN-SERIES":
				CarbonBlob.Serialize(BuildCreateTokenSeriesTx()).ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
			case "TX-MINT-NON-FUNGIBLE":
				CarbonBlob.Serialize(BuildMintNonFungibleTx()).ToHex().ShouldBe(row.Hex.ToUpperInvariant());
				break;
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
