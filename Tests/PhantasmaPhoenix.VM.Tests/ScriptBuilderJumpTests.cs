using PhantasmaPhoenix.Core.Extensions;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.VM.Tests;

// Targets of jumps and calls. The two bytes after a jump or a call hold the offset just past the
// label's NOP, little endian. The chain reads a jump target as a signed number, so a jump reaches
// offset 32767 at most, and a call target as unsigned.
public class ScriptBuilderJumpTests
{
	// A JMP takes 3 bytes and the label's NOP 1, so after `fill` NOPs the label sits at fill + 4.
	private static ScriptBuilder JumpOver(int fill) =>
		new ScriptBuilder().EmitJump(Opcode.JMP, "far").EmitRaw(new byte[fill]).EmitLabel("far");

	// A CALL takes 4 bytes and the label's NOP 1, so after `fill` NOPs the label sits at fill + 5.
	private static ScriptBuilder CallOver(int fill) =>
		new ScriptBuilder().EmitCall("far", 1).EmitRaw(new byte[fill]).EmitLabel("far");

	[Fact]
	public void A_forward_jump_lands_after_its_label()
	{
		new ScriptBuilder().EmitJump(Opcode.JMP, "end").Emit(Opcode.NOP).EmitLabel("end").EndScript()
			.ToHex().ShouldBe("08050000000B");
	}

	[Fact]
	public void A_jump_reaches_offset_32767_and_no_farther()
	{
		Should.NotThrow(() => JumpOver(32763).EndScript());
		Should.Throw<Exception>(() => JumpOver(32764).EndScript()).Message.ShouldContain("above 32767");
	}

	[Fact]
	public void A_call_reaches_offset_65535_and_no_farther()
	{
		// 32769 is 0x8001: beyond the jump limit, and still a valid call target.
		var script = CallOver(32764).EndScript();
		script[2].ShouldBe((byte)0x01);
		script[3].ShouldBe((byte)0x80);

		Should.NotThrow(() => CallOver(65530).EndScript());
		// 65536 does not fit two bytes. Without the check it would be cut to 0, a call to the start
		// of the script.
		Should.Throw<Exception>(() => CallOver(65531).EndScript()).Message.ShouldContain("above 65535");
	}
}
