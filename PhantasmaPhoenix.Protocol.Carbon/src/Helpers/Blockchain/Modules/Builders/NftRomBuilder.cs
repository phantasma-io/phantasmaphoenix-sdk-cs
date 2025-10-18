using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class NftRomBuilder
{
	public static byte[] BuildAndSerialize(BigInteger phantasmaNftId,
		string name,
		string description,
		string imageURL,
		string infoURL,
		uint royalties,
		byte[] rom, TokenSchemas? tokenSchemas)
	{
		var ts = tokenSchemas ?? TokenSchemasBuilder.PrepareStandardTokenSchemas();
		using MemoryStream romBuffer = new();
		using BinaryWriter wRom = new(romBuffer);
		new VmDynamicStruct
		{
			fields = new[]{
				new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(phantasmaNftId) },
				new VmNamedDynamicVariable{ name = new SmallString("name"), value = new VmDynamicVariable(name) },
				new VmNamedDynamicVariable{ name = new SmallString("description"), value = new VmDynamicVariable(description) },
				new VmNamedDynamicVariable{ name = new SmallString("imageURL"), value = new VmDynamicVariable(imageURL) },
				new VmNamedDynamicVariable{ name = new SmallString("infoURL"), value = new VmDynamicVariable(infoURL) },
				new VmNamedDynamicVariable{ name = new SmallString("royalties"), value = new VmDynamicVariable(royalties) },
				new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(rom) },
			}
		}.Write(ts.rom, wRom);

		return romBuffer.ToArray();
	}
}
