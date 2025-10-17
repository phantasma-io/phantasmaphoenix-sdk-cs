using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class SeriesInfoBuilder
{
	public static SeriesInfo Build(BigInteger phantasmaSeriesId, uint maxMint, uint maxSupply, Bytes32 ownerPublicKey, byte[]? metadata = null)
	{
		var m = metadata ?? TokenSeriesMetadataBuilder.BuildAndSerialize(phantasmaSeriesId, Array.Empty<byte>(), null);

		return new SeriesInfo
		{
			maxMint = maxMint, // limit on minting, or 0=no limit
			maxSupply = maxSupply, // limit on how many can exist at once
			owner = ownerPublicKey,
			metadata = m, // VmDynamicStruct encoded with TokenInfo.tokenSchemas.seriesMetadata
			rom = VmStructSchema.CreateEmpty(),
			ram = VmStructSchema.CreateEmpty()
		};
	}
}
