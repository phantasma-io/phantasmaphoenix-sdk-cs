using System.IO;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using Shouldly;
using Xunit;

namespace PhantasmaPhoenix.Protocol.Carbon.Tests;

public class TxMsgCallArgSectionsTests
{
	private const string ExpectedHex = "0100000002000000FEFFFFFFFFFFFFFF020000000A0B";

	[Fact]
	public void Encodes_and_decodes_arg_sections()
	{
		var call = new TxMsgCall
		{
			moduleId = 1,
			methodId = 2,
			args = Array.Empty<byte>(),
			sections = new MsgCallArgSections
			{
				argSections = new[]
				{
					new MsgCallArgs
					{
						registerOffset = -1,
						args = Array.Empty<byte>()
					},
					new MsgCallArgs
					{
						registerOffset = 0,
						args = new byte[] { 0x0A, 0x0B }
					}
				}
			}
		};

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		call.Write(writer);
		stream.ToArray().ToHex().ToUpperInvariant().ShouldBe(ExpectedHex);

		var bytes = ExpectedHex.FromHex() ?? Array.Empty<byte>();
		using var reader = new BinaryReader(new MemoryStream(bytes));
		var decoded = new TxMsgCall();
		decoded.Read(reader);

		decoded.moduleId.ShouldBe((uint)1);
		decoded.methodId.ShouldBe((uint)2);
		decoded.sections.HasSections.ShouldBeTrue();
		decoded.sections.argSections.ShouldNotBeNull();
		var sections = decoded.sections.argSections!;
		sections.Length.ShouldBe(2);
		sections[0].registerOffset.ShouldBe(-1);
		sections[0].args.Length.ShouldBe(0);
		sections[1].registerOffset.ShouldBe(0);
		sections[1].args.ToHex().ShouldBe("0A0B");
	}
}
