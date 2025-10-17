using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class NftRomBuilder
{
	public static byte[] BuildAndSerialize(BigInteger phantasmaNftId, byte[] rom, TokenSchemas? tokenSchemas)
	{
		var ts = tokenSchemas ?? TokenSchemasBuilder.PrepareStandardTokenSchemas();
		using MemoryStream romBuffer = new();
		using BinaryWriter wRom = new(romBuffer);
		new VmDynamicStruct
		{
			fields = new[]{
				new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(phantasmaNftId) },
				new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(rom) },
			}
		}.Write(ts.rom, wRom);

		return romBuffer.ToArray();
	}
}
