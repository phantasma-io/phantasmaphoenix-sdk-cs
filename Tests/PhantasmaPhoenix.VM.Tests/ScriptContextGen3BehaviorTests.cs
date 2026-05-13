using System.Numerics;
using PhantasmaPhoenix.Protocol;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.VM.Tests;

public class ScriptContextGen3BehaviorTests
{
	private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gen2_csharp_vm_scriptcontext_ops.tsv");

	public static IEnumerable<object[]> ScriptContextCases => ReadCases().Select(row => new object[] { row });

	public static IEnumerable<object[]> Gen3DivergenceCases => ReadCases(
		"shl_negative_count",
		"shr_negative_count",
		"or_enum_fault",
		"xor_enum_fault").Select(row => new object[] { row });

	[Theory]
	[MemberData(nameof(ScriptContextCases))]
	public void Script_context_ops_match_fixture_or_known_gen3_validator_policy(ScriptContextCase row)
	{
		// Execute every binary ScriptContext fixture row. Normal rows must match
		// the fixture descriptor; known Gen3 policy divergences must fault.
		var script = BuildBinaryOpScript(row);
		var vm = new GasMachine(script, 0);

		if (IsKnownGen3ValidatorDivergence(row.CaseId) || row.Outcome != "ok")
		{
			Should.Throw<VMException>(() => vm.Execute());
			vm.CurrentContext.ShouldBeOfType<ScriptContext>().Error.ShouldBe("Generic Error");
			return;
		}

		vm.Execute().ShouldBe(ExecutionState.Halt);
		Describe(vm.CurrentFrame.Registers[2]).ShouldBe(row.ExpectedDescriptor);
	}

	[Theory]
	[MemberData(nameof(Gen3DivergenceCases))]
	public void Gen3_script_context_divergences_match_validator_fault_policy(ScriptContextCase row)
	{
		// These rows intentionally diverge from permissive Gen2 behavior and
		// lock the current validator policy for negative shifts and enum OR/XOR.
		var script = BuildBinaryOpScript(row);
		var vm = new GasMachine(script, 0);

		var exception = Should.Throw<VMException>(() => vm.Execute());

		vm.CurrentContext.ShouldBeOfType<ScriptContext>().Error.ShouldBe("Generic Error");
		exception.Message.ShouldContain(ExpectedErrorFragment(row.CaseId));
	}

	private static byte[] BuildBinaryOpScript(ScriptContextCase row)
	{
		var builder = new ScriptBuilder();
		EmitLoad(builder, 0, row.LeftKind, row.LeftPayload);
		EmitLoad(builder, 1, row.RightKind, row.RightPayload);
		builder.Emit(row.Opcode, new byte[] { 0, 1, 2 });
		builder.Emit(Opcode.RET);
		return builder.ToScript();
	}

	private static void EmitLoad(ScriptBuilder builder, byte register, string kind, string payload)
	{
		switch (kind)
		{
			case "number":
				builder.EmitLoad(register, BigInteger.Parse(payload));
				break;
			case "string":
				builder.EmitLoad(register, payload);
				break;
			case "enum":
				builder.EmitLoad(register, BitConverter.GetBytes(uint.Parse(payload)), VMType.Enum);
				break;
			case "bool":
				builder.EmitLoad(register, bool.Parse(payload));
				break;
			default:
				throw new InvalidOperationException($"unsupported fixture operand kind {kind}");
		}
	}

	private static bool IsKnownGen3ValidatorDivergence(string caseId)
	{
		return caseId is "shl_negative_count" or "shr_negative_count" or "or_enum_fault" or "xor_enum_fault";
	}

	private static string Describe(VMObject obj)
	{
		return obj.Type switch
		{
			VMType.None => "None",
			VMType.Number => $"Number:{obj.AsNumber()}",
			VMType.Bool => $"Bool:{obj.AsBool().ToString().ToLowerInvariant()}",
			VMType.String => $"String:{obj.AsString()}",
			VMType.Enum => $"Enum:{obj.AsNumber()}",
			_ => $"{obj.Type}:{obj.AsString()}",
		};
	}

	private static string ExpectedErrorFragment(string caseId)
	{
		return caseId switch
		{
			"shl_negative_count" or "shr_negative_count" => "negative shift count is not supported",
			"or_enum_fault" or "xor_enum_fault" => "Enum OR/XOR are not supported",
			_ => throw new InvalidOperationException($"unexpected fixture case {caseId}"),
		};
	}

	private static IReadOnlyList<ScriptContextCase> ReadCases(params string[] caseIds)
	{
		var wanted = caseIds.Length > 0 ? caseIds.ToHashSet(StringComparer.Ordinal) : null;
		var rows = File.ReadLines(FixturePath)
			.Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#') && !line.StartsWith("case_id\t"))
			.Select(ParseRow)
			.Where(row => wanted == null || wanted.Contains(row.CaseId))
			.ToArray();

		if (wanted != null)
		{
			rows.Length.ShouldBe(caseIds.Length);
		}
		else
		{
			rows.Length.ShouldBeGreaterThan(0);
		}
		return rows;
	}

	private static ScriptContextCase ParseRow(string line)
	{
		var parts = line.Split('\t');
		parts.Length.ShouldBeGreaterThanOrEqualTo(12);
		return new ScriptContextCase(
			parts[0],
			Enum.Parse<Opcode>(parts[1]),
			parts[2],
			parts[4],
			parts[5],
			parts[7],
			parts[8],
			parts[11]);
	}

	public sealed record ScriptContextCase(
		string CaseId,
		Opcode Opcode,
		string LeftKind,
		string LeftPayload,
		string RightKind,
		string RightPayload,
		string Outcome,
		string ExpectedDescriptor);
}
