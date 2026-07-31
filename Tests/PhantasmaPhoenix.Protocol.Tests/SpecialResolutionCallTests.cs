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
		contract.Script.ShouldBe("010203");
		contract.RootVariables.ShouldHaveSingleItem().Value.ShouldBe("2223");
		var table = contract.Tables.ShouldHaveSingleItem();
		table.Name.ShouldBe("balances");
		table.Rows.ShouldHaveSingleItem().Key.ShouldBe("31");
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

		arguments.RawArgs.ShouldBe("0102FF");
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
}
