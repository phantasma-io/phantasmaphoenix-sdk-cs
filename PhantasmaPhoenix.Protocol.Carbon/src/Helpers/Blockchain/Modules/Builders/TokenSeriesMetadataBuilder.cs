using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenSeriesMetadataBuilder
{
	public static byte[] BuildAndSerialize(BigInteger phantasmaSeriesId, byte[] sharedRom, TokenSchemas? tokenSchemas)
	{
		// Write out the variables that are expected for a new series (encoded with respect to the seriesMetadataSchema used when creating the token)
		var ts = tokenSchemas ?? TokenSchemasBuilder.PrepareStandardTokenSchemas();
		using MemoryStream metadataBuffer = new();
		using BinaryWriter wMetadata = new(metadataBuffer);
		new VmDynamicStruct
		{
			fields = new[]{
				new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(phantasmaSeriesId) },
				new VmNamedDynamicVariable{ name = new SmallString("mode"), value = new VmDynamicVariable((byte)(sharedRom.Length == 0 ? 0 : 1)) },
				new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(sharedRom) },
			}
		}.Write(ts.seriesMetadata, wMetadata);

		return metadataBuffer.ToArray();
	}
}
