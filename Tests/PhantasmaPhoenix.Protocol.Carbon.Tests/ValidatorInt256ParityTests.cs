using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class ValidatorInt256ParityTests
{
	private static readonly ValidatorFixtureBundle Fixtures = LoadFixtures();
	private static readonly IReadOnlyDictionary<string, string> Int256ReadBackBySourceDec =
		Fixtures.Int256.ToDictionary(x => x.SourceDec, x => x.ReadBackSignedDec);

	public static IEnumerable<object[]> Int256Cases => Fixtures.Int256.Select(x => new object[] { x });
	public static IEnumerable<object[]> IntXCases => Fixtures.Intx.Select(x => new object[] { x });
	public static IEnumerable<object[]> VmDynamicInt256Cases => Fixtures.VmDynamicInt256.Select(x => new object[] { x });
	public static IEnumerable<object[]> VmDynamicInt256ArrayCases => Fixtures.VmDynamicInt256Array.Select(x => new object[] { x });
	public static IEnumerable<object[]> MetadataStructCases => Fixtures.MetadataStructs.Select(x => new object[] { x });
	public static IEnumerable<object[]> TokenInfoCases => Fixtures.TokenInfo.Select(x => new object[] { x });
	public static IEnumerable<object[]> SeriesInfoCases => Fixtures.SeriesInfo.Select(x => new object[] { x });

	// Raw Int256 fixtures pin the primitive wire contract that all higher-level Carbon serializers build on.
	[Theory]
	[MemberData(nameof(Int256Cases))]
	public void Int256_Encode_matches_validator_wire(Int256Fixture fixture)
	{
		SerializeBigInt(ParseBigInteger(fixture.SourceDec)).ToHex().ShouldBe(fixture.WireHex);
	}

	// Decode coverage keeps the validator's signed readback semantics explicit, including the shortest negative form.
	[Theory]
	[MemberData(nameof(Int256Cases))]
	public void Int256_Decode_matches_validator_readback(Int256Fixture fixture)
	{
		fixture.WireHex.FromHex()!.ReadBigInt().ToString().ShouldBe(fixture.ReadBackSignedDec);
	}

	// IntX reuses the same BigInt payload rules but has its own compact/int64 crossover behavior to preserve.
	[Theory]
	[MemberData(nameof(IntXCases))]
	public void IntX_Encode_matches_validator_wire(IntXFixture fixture)
	{
		CarbonBlob.Serialize(new IntX(ParseBigInteger(fixture.SourceDec))).ToHex().ShouldBe(fixture.WireHex);
	}

	// Readback assertions guard the boundary between compact Int64 storage and big IntX payloads.
	[Theory]
	[MemberData(nameof(IntXCases))]
	public void IntX_Decode_matches_validator_readback(IntXFixture fixture)
	{
		CarbonBlob.New<IntX>(fixture.WireHex.FromHex()!).ToString().ShouldBe(fixture.ReadBackDec);
	}

	// VmDynamicVariable(Int256) is the direct RPC/storage-facing wrapper that originally exposed the mismatch.
	[Theory]
	[MemberData(nameof(VmDynamicInt256Cases))]
	public void VmDynamicInt256_Encode_matches_validator_wire(VmDynamicInt256Fixture fixture)
	{
		CarbonBlob.Serialize(new VmDynamicVariable(ParseBigInteger(fixture.SourceDec))).ToHex().ShouldBe(fixture.WireHex);
	}

	// The wrapper read path must reconstruct the same signed values the validator derives from the wire bytes.
	[Theory]
	[MemberData(nameof(VmDynamicInt256Cases))]
	public void VmDynamicInt256_Decode_matches_validator_readback(VmDynamicInt256Fixture fixture)
	{
		var decoded = CarbonBlob.New<VmDynamicVariable>(fixture.WireHex.FromHex()!);

		decoded.type.ShouldBe(VmType.Int256);
		decoded.GetInt256().ToString().ShouldBe(ExpectedInt256ReadBack(fixture.SourceDec));
	}

	// Array coverage ensures the validator-aligned Int256 rules survive list framing, not just scalar values.
	[Theory]
	[MemberData(nameof(VmDynamicInt256ArrayCases))]
	public void VmDynamicInt256Array_Encode_matches_validator_wire(VmDynamicInt256ArrayFixture fixture)
	{
		var value = new VmDynamicVariable
		{
			type = VmType.Array | VmType.Int256,
			data = fixture.Values.Select(ParseBigInteger).ToArray()
		};

		CarbonBlob.Serialize(value).ToHex().ShouldBe(fixture.WireHex);
	}

	// The array read path previously hid some `0x80 -> -1` cases, so keep those expectations anchored here.
	[Theory]
	[MemberData(nameof(VmDynamicInt256ArrayCases))]
	public void VmDynamicInt256Array_Decode_matches_validator_readback(VmDynamicInt256ArrayFixture fixture)
	{
		var decoded = CarbonBlob.New<VmDynamicVariable>(fixture.WireHex.FromHex()!);

		decoded.type.ShouldBe(VmType.Array | VmType.Int256);
		var values = ((BigInteger[])decoded.data!).Select(x => x.ToString()).ToArray();
		values.ShouldBe(fixture.Values.Select(ExpectedInt256ReadBack).ToArray());
	}

	// Metadata structs protect the nested `_i` field shape used by legacy/current NFT and series metadata.
	[Theory]
	[MemberData(nameof(MetadataStructCases))]
	public void MetadataStruct_Encode_matches_validator_wire(MetadataStructFixture fixture)
	{
		CarbonBlob.Serialize(BuildMetadataStruct(fixture)).ToHex().ShouldBe(fixture.WireHex);
	}

	// Decode assertions keep the schema-sensitive `_i` field and ROM payload layout aligned with validator fixtures.
	[Theory]
	[MemberData(nameof(MetadataStructCases))]
	public void MetadataStruct_Decode_matches_fixture_shape(MetadataStructFixture fixture)
	{
		var decoded = CarbonBlob.New<VmDynamicStruct>(fixture.WireHex.FromHex()!);

		if (fixture.Shape == "nft-default")
		{
			decoded.fields.Select(x => x.name.data).ToArray().ShouldBe(new[] { "_i", "rom" });
		}
		else
		{
			decoded.fields.Select(x => x.name.data).ToArray().ShouldBe(new[] { "_i", "mode", "rom" });
		}

		var idField = decoded["_i"];
		idField.ShouldNotBeNull();
		idField.Value.type.ShouldBe(VmType.Int256);
		idField.Value.GetInt256().ToString().ShouldBe(fixture.MetaIdDec);

		var romField = decoded["rom"];
		romField.ShouldNotBeNull();
		romField.Value.type.ShouldBe(VmType.Bytes);
		romField.Value.GetBytes().ToHex().ShouldBe(fixture.RomHex);

		if (fixture.Mode.HasValue)
		{
			var modeField = decoded["mode"];
			modeField.ShouldNotBeNull();
			modeField.Value.type.ShouldBe(VmType.Int8);
			modeField.Value.GetUInt8().ShouldBe((byte)fixture.Mode.Value);
		}
	}

	// TokenInfo fixtures prove that protocol-level IntX and metadata changes still compose into real token records.
	[Theory]
	[MemberData(nameof(TokenInfoCases))]
	public void TokenInfo_Encode_matches_validator_wire(TokenInfoFixture fixture)
	{
		CarbonBlob.Serialize(BuildTokenInfo(fixture)).ToHex().ShouldBe(fixture.WireHex);
	}

	// Decoding TokenInfo guards the higher-level fields that RPC consumers actually read from stored bytes.
	[Theory]
	[MemberData(nameof(TokenInfoCases))]
	public void TokenInfo_Decode_matches_fixture_fields(TokenInfoFixture fixture)
	{
		var decoded = CarbonBlob.New<TokenInfo>(fixture.WireHex.FromHex()!);

		((BigInteger)decoded.maxSupply).ToString().ShouldBe(fixture.MaxSupplyDec);
		decoded.flags.ShouldBe((TokenFlags)fixture.Flags);
		decoded.decimals.ShouldBe((uint)fixture.Decimals);
		decoded.owner.ShouldBe(ExpectedTokenOwner(fixture.Id));
		decoded.symbol.data.ShouldBe(fixture.Symbol);
		decoded.metadata.ToHex().ShouldBe(fixture.MetadataHex);
	}

	// SeriesInfo fixtures cover the last higher-level protocol object touched by the problematic `_i` metadata bytes.
	[Theory]
	[MemberData(nameof(SeriesInfoCases))]
	public void SeriesInfo_Encode_matches_validator_wire(SeriesInfoFixture fixture)
	{
		CarbonBlob.Serialize(BuildSeriesInfo(fixture)).ToHex().ShouldBe(fixture.WireHex);
	}

	// Readback checks keep the series metadata `_i` field and ownership bytes stable for RPC-facing reads.
	[Theory]
	[MemberData(nameof(SeriesInfoCases))]
	public void SeriesInfo_Decode_matches_fixture_fields(SeriesInfoFixture fixture)
	{
		var decoded = CarbonBlob.New<SeriesInfo>(fixture.WireHex.FromHex()!);

		decoded.maxMint.ShouldBe(fixture.MaxMint);
		decoded.maxSupply.ShouldBe(fixture.MaxSupply);
		decoded.owner.ShouldBe(ExpectedSeriesOwner(fixture.Id));
		decoded.metadata.ToHex().ShouldBe(fixture.MetadataHex);
		decoded.rom.fields.Length.ShouldBe(0);
		decoded.ram.fields.Length.ShouldBe(0);

		var metadata = CarbonBlob.New<VmDynamicStruct>(decoded.metadata);
		var idField = metadata["_i"];
		idField.ShouldNotBeNull();
		idField.Value.GetInt256().ToString().ShouldBe(ExpectedMetadataId(fixture.Id));
	}

	private static byte[] SerializeBigInt(BigInteger value)
	{
		using MemoryStream buffer = new();
		using BinaryWriter writer = new(buffer);
		writer.WriteBigInt(value);
		return buffer.ToArray();
	}

	private static BigInteger ParseBigInteger(string value) => BigInteger.Parse(value);

	private static string ExpectedInt256ReadBack(string sourceDec) => Int256ReadBackBySourceDec[sourceDec];

	private static VmDynamicStruct BuildMetadataStruct(MetadataStructFixture fixture)
	{
		var fields = new List<VmNamedDynamicVariable>
		{
			new()
			{
				name = new SmallString("_i"),
				value = new VmDynamicVariable(ParseBigInteger(fixture.MetaIdDec))
			},
			new()
			{
				name = new SmallString("rom"),
				value = new VmDynamicVariable(fixture.RomHex.FromHex() ?? Array.Empty<byte>())
			}
		};

		if (fixture.Mode.HasValue)
		{
			fields.Add(new VmNamedDynamicVariable
			{
				name = new SmallString("mode"),
				value = new VmDynamicVariable((byte)fixture.Mode.Value)
			});
		}

		return new VmDynamicStruct { fields = fields.ToArray() };
	}

	private static TokenInfo BuildTokenInfo(TokenInfoFixture fixture)
	{
		return new TokenInfo
		{
			maxSupply = new IntX(ParseBigInteger(fixture.MaxSupplyDec)),
			flags = (TokenFlags)fixture.Flags,
			decimals = (uint)fixture.Decimals,
			owner = ExpectedTokenOwner(fixture.Id),
			symbol = new SmallString(fixture.Symbol),
			metadata = fixture.MetadataHex.FromHex() ?? Array.Empty<byte>(),
			tokenSchemas = Array.Empty<byte>()
		};
	}

	private static SeriesInfo BuildSeriesInfo(SeriesInfoFixture fixture)
	{
		return new SeriesInfo
		{
			maxMint = fixture.MaxMint,
			maxSupply = fixture.MaxSupply,
			owner = ExpectedSeriesOwner(fixture.Id),
			metadata = fixture.MetadataHex.FromHex() ?? Array.Empty<byte>(),
			rom = VmStructSchema.CreateEmpty(),
			ram = VmStructSchema.CreateEmpty()
		};
	}

	private static Bytes32 ExpectedTokenOwner(string id) => id switch
	{
		"fungible_zero_supply" => PatternBytes32(0x10),
		"big_fungible_u64max_supply" => PatternBytes32(0x20),
		_ => throw new InvalidOperationException($"Unknown token fixture id: {id}")
	};

	private static Bytes32 ExpectedSeriesOwner(string id) => id switch
	{
		"series_zero_metaid" => PatternBytes32(0x30),
		"series_problematic_metaid" => PatternBytes32(0x40),
		_ => throw new InvalidOperationException($"Unknown series fixture id: {id}")
	};

	private static string ExpectedMetadataId(string id) => id switch
	{
		"series_zero_metaid" => "0",
		"series_problematic_metaid" => "342701406799689386264365071881606655601301200422094937311139938246178500459",
		_ => throw new InvalidOperationException($"Unknown series fixture id: {id}")
	};

	private static Bytes32 PatternBytes32(byte seed)
	{
		var bytes = new byte[32];
		for (var i = 0; i < bytes.Length; i++)
		{
			bytes[i] = (byte)(seed + i);
		}
		return new Bytes32(bytes);
	}

	private static ValidatorFixtureBundle LoadFixtures()
	{
		var path = ResolveFixturePath();
		var json = File.ReadAllText(path);
		var bundle = JsonSerializer.Deserialize<ValidatorFixtureBundle>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
		return bundle ?? throw new InvalidOperationException($"Failed to deserialize validator fixtures from {path}");
	}

	private static string ResolveFixturePath()
	{
		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, "validator_int256_fixtures.json"),
			Path.Combine(AppContext.BaseDirectory, "Fixtures", "validator_int256_fixtures.json")
		};

		foreach (var path in candidates)
		{
			if (File.Exists(path))
			{
				return path;
			}
		}

		throw new FileNotFoundException("validator_int256_fixtures.json not found", string.Join(", ", candidates));
	}

	public sealed class ValidatorFixtureBundle
	{
		[JsonPropertyName("int256")]
		public List<Int256Fixture> Int256 { get; set; } = new();

		[JsonPropertyName("intx")]
		public List<IntXFixture> Intx { get; set; } = new();

		[JsonPropertyName("vmDynamicInt256")]
		public List<VmDynamicInt256Fixture> VmDynamicInt256 { get; set; } = new();

		[JsonPropertyName("vmDynamicInt256Array")]
		public List<VmDynamicInt256ArrayFixture> VmDynamicInt256Array { get; set; } = new();

		[JsonPropertyName("metadataStructs")]
		public List<MetadataStructFixture> MetadataStructs { get; set; } = new();

		[JsonPropertyName("tokenInfo")]
		public List<TokenInfoFixture> TokenInfo { get; set; } = new();

		[JsonPropertyName("seriesInfo")]
		public List<SeriesInfoFixture> SeriesInfo { get; set; } = new();
	}

	public sealed class Int256Fixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("sourceDec")]
		public string SourceDec { get; set; } = "";

		[JsonPropertyName("readBackSignedDec")]
		public string ReadBackSignedDec { get; set; } = "";

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}

	public sealed class IntXFixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("sourceDec")]
		public string SourceDec { get; set; } = "";

		[JsonPropertyName("readBackDec")]
		public string ReadBackDec { get; set; } = "";

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}

	public sealed class VmDynamicInt256Fixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("sourceDec")]
		public string SourceDec { get; set; } = "";

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}

	public sealed class VmDynamicInt256ArrayFixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("values")]
		public List<string> Values { get; set; } = new();

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}

	public sealed class MetadataStructFixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("shape")]
		public string Shape { get; set; } = "";

		[JsonPropertyName("_iDec")]
		public string MetaIdDec { get; set; } = "";

		[JsonPropertyName("mode")]
		public int? Mode { get; set; }

		[JsonPropertyName("romHex")]
		public string RomHex { get; set; } = "";

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}

	public sealed class TokenInfoFixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("maxSupplyDec")]
		public string MaxSupplyDec { get; set; } = "";

		[JsonPropertyName("flags")]
		public int Flags { get; set; }

		[JsonPropertyName("decimals")]
		public int Decimals { get; set; }

		[JsonPropertyName("symbol")]
		public string Symbol { get; set; } = "";

		[JsonPropertyName("metadataHex")]
		public string MetadataHex { get; set; } = "";

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}

	public sealed class SeriesInfoFixture
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = "";

		[JsonPropertyName("maxMint")]
		public uint MaxMint { get; set; }

		[JsonPropertyName("maxSupply")]
		public uint MaxSupply { get; set; }

		[JsonPropertyName("metadataHex")]
		public string MetadataHex { get; set; } = "";

		[JsonPropertyName("wireHex")]
		public string WireHex { get; set; } = "";
	}
}
