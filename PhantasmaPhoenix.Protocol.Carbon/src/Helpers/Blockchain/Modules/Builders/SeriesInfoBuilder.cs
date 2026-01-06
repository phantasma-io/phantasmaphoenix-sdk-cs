using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class SeriesInfoBuilder
{
	public static SeriesInfo Build(BigInteger phantasmaSeriesId, uint maxMint, uint maxSupply, Bytes32 ownerPublicKey, byte[]? metadata = null)
	{
		if (metadata == null)
		{
			var tokenSchemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
			metadata = TokenSeriesMetadataBuilder.BuildAndSerialize(
				tokenSchemas.seriesMetadata,
				phantasmaSeriesId,
				Array.Empty<MetadataField>());
		}

		return new SeriesInfo
		{
			maxMint = maxMint, // limit on minting, or 0=no limit
			maxSupply = maxSupply, // limit on how many can exist at once
			owner = ownerPublicKey,
			metadata = metadata, // VmDynamicStruct encoded with TokenInfo.tokenSchemas.seriesMetadata
			rom = VmStructSchema.CreateEmpty(),
			ram = VmStructSchema.CreateEmpty()
		};
	}
}
