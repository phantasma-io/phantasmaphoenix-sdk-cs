using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class NftRomBuilder
{
	public static byte[] BuildAndSerialize(
		VmStructSchema nftRomSchema,
		BigInteger phantasmaNftId,
		IReadOnlyList<MetadataField> metadata)
	{
		if (metadata == null)
		{
			throw new ArgumentNullException(nameof(metadata));
		}

		var rom = MetadataHelper.GetOptionalBytesField(metadata, "rom");

		var fields = new List<VmNamedDynamicVariable>
		{
			new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(phantasmaNftId) },
			new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(rom) },
		};

		var defaultFieldNames = new HashSet<string>(
			MetadataHelper.NftDefaultMetadataFields.Select(f => f.Name),
			StringComparer.Ordinal);

		foreach (var schemaField in nftRomSchema.fields ?? Array.Empty<VmNamedVariableSchema>())
		{
			if (defaultFieldNames.Contains(schemaField.name.data))
			{
				continue;
			}

			MetadataHelper.PushMetadataField(schemaField, fields, metadata);
		}

		using MemoryStream romBuffer = new();
		using BinaryWriter wRom = new(romBuffer);
		new VmDynamicStruct
		{
			fields = fields.ToArray()
		}.Write(nftRomSchema, wRom);

		return romBuffer.ToArray();
	}

}
