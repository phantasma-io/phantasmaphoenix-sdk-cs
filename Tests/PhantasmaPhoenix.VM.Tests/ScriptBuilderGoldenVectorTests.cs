using System.Numerics;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.VM.Tests;

public class ScriptBuilderGoldenVectorTests
{
	private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "vm_script_builder_vectors.tsv");

	public static IEnumerable<object[]> ScriptBuilderCases => ReadRows().Select(row => new object[] { row });

	[Theory]
	[MemberData(nameof(ScriptBuilderCases))]
	public void ScriptBuilder_matches_shared_golden_vectors(ScriptBuilderRow row)
	{
		// Rebuild each script through public ScriptBuilder APIs and compare the
		// final bytecode, which catches ABI and opcode-encoding regressions.
		row.Source.ShouldBe("csharp_sdk");
		BuildScript(row.CaseId).ToHex().ShouldBe(row.ExpectedHex);
		row.Notes.ShouldNotBeEmpty();
	}

	private static byte[] BuildScript(string caseId)
	{
		var mainKeys = PhantasmaKeys.FromWIF("L5UEVHBjujaR1721aZM5Zm5ayjDyamMZS9W35RE9Y9giRkdf3dVx");
		var helperKeys = PhantasmaKeys.FromWIF("KxMn2TgXukYaNXx7tEdjh7qB2YaMgeuKy47j4rvKigHhBuZWeP3r");
		var address = helperKeys.Address;
		var nullAddress = Address.Null;

		return caseId switch
		{
			"consensus_single_vote" => new ScriptBuilder()
				.AllowGas(mainKeys.Address, nullAddress, 10000, 210000)
				.CallContract("consensus", "SingleVote", mainKeys.Address.Text, "system.nexus.protocol.version", 0)
				.SpendGas(mainKeys.Address)
				.EndScript(),
			"gas_transfer_spend" => new ScriptBuilder()
				.AllowGas(address, nullAddress, 100000, 21000)
				.TransferTokens("SOUL", address, nullAddress, 100000000)
				.SpendGas(address)
				.EndScript(),
			"mint_tokens" => new ScriptBuilder()
				.MintTokens("SOUL", address, nullAddress, BigInteger.One)
				.EndScript(),
			"transfer_balance" => new ScriptBuilder()
				.TransferBalance("KCAL", address, nullAddress)
				.EndScript(),
			"transfer_nft" => new ScriptBuilder()
				.TransferNFT("ART", address, nullAddress, new BigInteger(42))
				.EndScript(),
			"cross_transfer_token" => new ScriptBuilder()
				.CrossTransferToken(nullAddress, "SOUL", address, nullAddress, BigInteger.One)
				.EndScript(),
			"cross_transfer_nft" => new ScriptBuilder()
				.CrossTransferNFT(nullAddress, "ART", address, nullAddress, new BigInteger(7))
				.EndScript(),
			"stake_unstake" => new ScriptBuilder()
				.CallContract("stake", "Stake", address, new BigInteger(7))
				.CallContract("stake", "Unstake", address, new BigInteger(8))
				.EndScript(),
			"call_nft" => new ScriptBuilder()
				.CallNFT("ART", new BigInteger(7), "mint", address)
				.EndScript(),
			"runtime_array_timestamp" => new ScriptBuilder()
				.CallInterop("Runtime.Test", new object[] { "alpha", new BigInteger(7) }, new Timestamp(1778330400))
				.EndScript(),
			_ => throw new InvalidOperationException($"unhandled script vector {caseId}"),
		};
	}

	private static IReadOnlyList<ScriptBuilderRow> ReadRows()
	{
		return File.ReadLines(FixturePath)
			.Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("case_id\t"))
			.Select(line =>
			{
				var parts = line.Split('\t');
				parts.Length.ShouldBe(4);
				return new ScriptBuilderRow(parts[0], parts[1], parts[2], parts[3]);
			})
			.ToArray();
	}

	public sealed record ScriptBuilderRow(string CaseId, string Source, string ExpectedHex, string Notes);
}
