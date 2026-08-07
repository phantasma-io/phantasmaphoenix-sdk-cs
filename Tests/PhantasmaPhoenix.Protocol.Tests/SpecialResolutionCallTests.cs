using Newtonsoft.Json;
using PhantasmaPhoenix.Protocol.ExtendedEvents;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Tests;

// A special resolution answer types its call arguments by the method that was called. These tests use
// the shape a node actually answers with, so they fail if either side of that contract drifts.
public class SpecialResolutionCallTests
{
	[Fact]
	public void ImportContracts_arguments_deserialize_into_the_typed_model()
	{
		const string json = """
		{
			"moduleId": 2,
			"module": "phantasma_vm",
			"methodId": 5,
			"method": "ImportContracts",
			"arguments": {
				"contractsCount": "1",
				"contracts": [
					{
						"name": "pharming",
						"address": "S3d6cUXRwJbudV4ADbRtMz3P9527ts7D2Lh9h2J96m48FPW",
						"owner": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8",
						"script": "010203",
						"abi": "0A",
						"rootVariables": [ { "key": "11", "value": "2223" } ],
						"tables": [
							{ "name": "balances", "rows": [ { "key": "31", "value": "4142" } ] }
						]
					}
				]
			}
		}
		""";

		var call = JsonConvert.DeserializeObject<SpecialResolutionCall>(json);

		call.Method.ShouldBe("ImportContracts");
		var arguments = call.Arguments.ShouldBeOfType<ImportContractsArguments>();
		arguments.ContractsCount.ShouldBe("1");
		var contract = arguments.Contracts.ShouldHaveSingleItem();
		contract.Name.ShouldBe("pharming");
		contract.Script.ToString().ShouldBe("010203");
		contract.RootVariables.ShouldHaveSingleItem().Value.ToString().ShouldBe("2223");
		var table = contract.Tables.ShouldHaveSingleItem();
		table.Name.ShouldBe("balances");
		table.Rows.ShouldHaveSingleItem().Key.ToString().ShouldBe("31");
	}

	[Fact]
	public void Token_call_arguments_deserialize_into_the_typed_model()
	{
		const string json = """
		{
			"moduleId": 1,
			"module": "token",
			"methodId": 0,
			"method": "TransferFungible",
			"arguments": {
				"token": "SOUL",
				"tokenId": "1",
				"from": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8",
				"to": "S3d6cUXRwJbudV4ADbRtMz3P9527ts7D2Lh9h2J96m48FPW",
				"amount": "1000000000000000000000"
			}
		}
		""";

		var arguments = JsonConvert.DeserializeObject<SpecialResolutionCall>(json)
			.Arguments.ShouldBeOfType<TransferFungibleArguments>();

		arguments.Token.ShouldBe("SOUL");
		// Amounts stay strings on purpose: this one has no exact double representation.
		arguments.Amount.ShouldBe("1000000000000000000000");
	}

	// A method the answering node could not decode arrives as the raw argument buffer. It must never be
	// read as the typed shape, which would hand the caller an object with every field empty.
	[Fact]
	public void Undecoded_arguments_stay_raw()
	{
		const string json = """
		{
			"moduleId": 0,
			"module": "governance",
			"methodId": 12,
			"method": "MigrateAddresses",
			"arguments": { "rawArgs": "0102FF" }
		}
		""";

		var arguments = JsonConvert.DeserializeObject<SpecialResolutionCall>(json)
			.Arguments.ShouldBeOfType<RawArguments>();

		arguments.RawArgs.ToString().ShouldBe("0102FF");
	}

	// Same rule when the method IS in the map: an older node answering with a raw dump must not be
	// mistaken for a decoded call.
	[Fact]
	public void Raw_arguments_win_over_the_method_map()
	{
		const string json = """
		{
			"moduleId": 2,
			"module": "phantasma_vm",
			"methodId": 5,
			"method": "ImportContracts",
			"arguments": { "rawArgs": "00" }
		}
		""";

		JsonConvert.DeserializeObject<SpecialResolutionCall>(json)
			.Arguments.ShouldBeOfType<RawArguments>();
	}

	[Fact]
	public void Nested_resolution_calls_are_typed_too()
	{
		const string json = """
		{
			"moduleId": 0,
			"module": "governance",
			"methodId": 2,
			"method": "SpecialResolution",
			"arguments": { "resolutionId": "35" },
			"calls": [
				{
					"moduleId": 0,
					"module": "governance",
					"methodId": 8,
					"method": "LookupName",
					"arguments": { "address": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8" }
				}
			]
		}
		""";

		var call = JsonConvert.DeserializeObject<SpecialResolutionCall>(json);

		call.Arguments.ShouldBeOfType<NestedResolutionArguments>().ResolutionId.ShouldBe("35");
		var nested = call.Calls.ShouldHaveSingleItem();
		nested.Arguments.ShouldBeOfType<AddressArguments>()
			.Address.ShouldBe("P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8");
	}

	// The heavy fields are carried as bytes in memory (HexBytes), but the wire is the hex string it
	// always was: a parse-serialize round trip must reproduce the hex verbatim, or the type change
	// would leak onto the wire.
	[Fact]
	public void Heavy_fields_round_trip_as_the_same_hex_strings()
	{
		const string json = """
		{
			"moduleId": 2,
			"module": "phantasma_vm",
			"methodId": 5,
			"method": "ImportContracts",
			"arguments": {
				"contractsCount": "1",
				"contracts": [
					{
						"name": "pharming",
						"address": "S3d6cUXRwJbudV4ADbRtMz3P9527ts7D2Lh9h2J96m48FPW",
						"owner": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8",
						"script": "010203",
						"abi": "0A",
						"rootVariables": [ { "key": "11", "value": "2223" } ],
						"tables": [
							{ "name": "balances", "rows": [ { "key": "31", "value": "4142" } ] }
						]
					}
				]
			}
		}
		""";

		var serialized = SerializeAsANodeWould(JsonConvert.DeserializeObject<SpecialResolutionCall>(json));

		var reparsed = JsonConvert.DeserializeObject<SpecialResolutionCall>(serialized);
		var contract = reparsed.Arguments.ShouldBeOfType<ImportContractsArguments>()
			.Contracts.ShouldHaveSingleItem();
		contract.Script.ToString().ShouldBe("010203");
		contract.Abi.ToString().ShouldBe("0A");
		contract.Tables.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem().Value.ToString().ShouldBe("4142");
	}

	// Blob ARRAYS are the one shape a member-level converter does not cover: the attribute would bind
	// to the array itself rather than to its elements, and the field would serialize as a list of
	// objects instead of hex strings - a silent wire break no round trip through our own reader would
	// notice. So the assertion is on the serialized text, not only on the reparsed model.
	[Fact]
	public void Blob_arrays_stay_arrays_of_hex_strings()
	{
		const string json = """
		{
			"moduleId": 1,
			"module": "token",
			"methodId": 30,
			"method": "CreateMintedTokenSeries",
			"arguments": {
				"token": "GHOST",
				"tokenId": "4",
				"owner": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8",
				"maxMint": "10",
				"maxSupply": "10",
				"recipient": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8",
				"metadataRaw": "AABB",
				"roms": [ "0102", "0304" ],
				"rams": [ "05", "" ]
			}
		}
		""";

		var serialized = SerializeAsANodeWould(JsonConvert.DeserializeObject<SpecialResolutionCall>(json));

		serialized.ShouldContain("\"roms\":[\"0102\",\"0304\"]");
		serialized.ShouldContain("\"rams\":[\"05\",\"\"]");

		var arguments = JsonConvert.DeserializeObject<SpecialResolutionCall>(serialized)
			.Arguments.ShouldBeOfType<CreateMintedTokenSeriesArguments>();
		arguments.Roms.Length.ShouldBe(2);
		arguments.Roms[0].ToString().ShouldBe("0102");
		arguments.Roms[1].ToString().ShouldBe("0304");
		arguments.Rams[0].ToString().ShouldBe("05");
		arguments.Rams[1].ToString().ShouldBe("");
		arguments.MetadataRaw!.ToString().ShouldBe("AABB");
	}

	// The same byte-typed contract on the governance module: a contract deployment carries the two
	// largest blobs in the family, and they must reach the wire as the hex strings they always were.
	[Fact]
	public void Governance_contract_blobs_round_trip_as_hex_strings()
	{
		const string json = """
		{
			"moduleId": 2,
			"module": "phantasma_vm",
			"methodId": 3,
			"method": "DeployContract",
			"arguments": {
				"from": "P2KFNXEbt65rQiWqogAzqkVGMqFirPmqPw8mQyxvRKsrXV8",
				"contractName": "pharming",
				"script": "0102FF",
				"abi": "0A0B"
			}
		}
		""";

		var serialized = SerializeAsANodeWould(JsonConvert.DeserializeObject<SpecialResolutionCall>(json));

		serialized.ShouldContain("\"script\":\"0102FF\"");
		serialized.ShouldContain("\"abi\":\"0A0B\"");

		var arguments = JsonConvert.DeserializeObject<SpecialResolutionCall>(serialized)
			.Arguments.ShouldBeOfType<DeployContractArguments>();
		arguments.Script.ToString().ShouldBe("0102FF");
		arguments.Abi.ToString().ShouldBe("0A0B");
	}

	// The call model is read-only for its converter; a node serializes it by property contract with
	// camel-cased names. Mirror that here, or a round trip would emit Pascal-cased JSON no reader
	// recognizes.
	private static string SerializeAsANodeWould(SpecialResolutionCall call)
	{
		return JsonConvert.SerializeObject(call, new JsonSerializerSettings
		{
			ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
			{
				NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
			}
		});
	}
}
