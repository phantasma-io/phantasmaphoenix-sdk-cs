using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class TokenBuilderValidationTests
{
	private const string SamplePngIconDataUri =
		"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";

	[Fact]
	public void TokenInfoBuilder_rejects_invalid_symbol()
	{
		var metadata = BuildTokenMetadata();
		var creator = Bytes32.Empty;
		var maxSupply = new IntX(0);

		Should.Throw<ArgumentException>(() =>
			TokenInfoBuilder.Build(string.Empty, maxSupply, false, 0, creator, metadata))
			.Message.ShouldContain("Symbol validation error: Empty string is invalid");

		Should.Throw<ArgumentException>(() =>
			TokenInfoBuilder.Build(new string('A', 256), maxSupply, false, 0, creator, metadata))
			.Message.ShouldContain("Symbol validation error: Too long");

		Should.Throw<ArgumentException>(() =>
			TokenInfoBuilder.Build("AB1", maxSupply, false, 0, creator, metadata))
			.Message.ShouldContain("Symbol validation error: Anything outside A-Z is forbidden");
	}

	[Fact]
	public void TokenInfoBuilder_requires_metadata()
	{
		var creator = Bytes32.Empty;

		Should.Throw<ArgumentException>(() =>
			TokenInfoBuilder.Build("TEST", new IntX(0), false, 0, creator, null))
			.Message.ShouldContain("metadata is required");
	}

	[Fact]
	public void TokenInfoBuilder_nft_constraints_are_enforced()
	{
		var metadata = BuildTokenMetadata();
		var creator = Bytes32.Empty;
		var tokenSchemas = TokenSchemasBuilder.BuildAndSerialize(null);

		var bigSupply = new IntX(BigInteger.Parse("9223372036854775808"));
		Should.Throw<ArgumentException>(() =>
			TokenInfoBuilder.Build("NFT", bigSupply, true, 0, creator, metadata, tokenSchemas))
			.Message.ShouldContain("NFT maximum supply must fit into Int64");

		Should.Throw<ArgumentException>(() =>
			TokenInfoBuilder.Build("NFT", new IntX(0), true, 0, creator, metadata, null))
			.Message.ShouldContain("tokenSchemas is required for NFTs");
	}

	[Fact]
	public void TokenInfoBuilder_accepts_valid_fungible()
	{
		var metadata = BuildTokenMetadata();
		var creator = Bytes32.Empty;

		Should.NotThrow(() =>
			TokenInfoBuilder.Build("FUNGIBLE", new IntX(0), false, 8, creator, metadata));
	}

	[Fact]
	public void TokenSchemasBuilder_reports_missing_standard_metadata()
	{
		var schemas = new TokenSchemas
		{
			seriesMetadata = BuildSchema(MetadataHelper.SeriesDefaultMetadataFields),
			rom = BuildSchema(MetadataHelper.NftDefaultMetadataFields),
			ram = VmStructSchema.CreateEmpty()
		};

		var result = TokenSchemasBuilder.Verify(schemas);
		result.ok.ShouldBeFalse();
		result.error.ShouldBe("Mandatory metadata field not found: name");
	}

	[Fact]
	public void TokenSchemasBuilder_reports_type_mismatch()
	{
		var schemas = new TokenSchemas
		{
			seriesMetadata = BuildSchema(new[]
			{
				new FieldType("name", VmType.Int32)
			}),
			rom = VmStructSchema.CreateEmpty(),
			ram = VmStructSchema.CreateEmpty()
		};

		var result = TokenSchemasBuilder.Verify(schemas);
		result.ok.ShouldBeFalse();
		result.error.ShouldNotBeNull();
		result.error.ShouldContain("Type mismatch for name field");
	}

	[Fact]
	public void TokenSchemasBuilder_reports_case_mismatch()
	{
		var schemas = new TokenSchemas
		{
			seriesMetadata = BuildSchema(new[]
			{
				new FieldType("Name", VmType.String)
			}),
			rom = VmStructSchema.CreateEmpty(),
			ram = VmStructSchema.CreateEmpty()
		};

		var result = TokenSchemasBuilder.Verify(schemas);
		result.ok.ShouldBeFalse();
		result.error.ShouldNotBeNull();
		result.error.ShouldContain("Case mismatch for name field");
	}

	private static byte[] BuildTokenMetadata()
	{
		var fields = new Dictionary<string, string>
		{
			["name"] = "My test token!",
			["icon"] = SamplePngIconDataUri,
			["url"] = "http://example.com",
			["description"] = "My test token description"
		};

		return TokenMetadataBuilder.BuildAndSerialize(fields);
	}

	private static VmStructSchema BuildSchema(IEnumerable<FieldType> fields)
	{
		var schemaFields = fields
			.Select(f => new VmNamedVariableSchema
			{
				name = new SmallString(f.Name),
				schema = new VmVariableSchema { type = f.Type }
			})
			.ToArray();

		return new VmStructSchema
		{
			fields = schemaFields,
			flags = VmStructSchema.Flags.None
		};
	}
}
