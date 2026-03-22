using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class PhantasmaNftRomBuilder
{
	private static readonly HashSet<string> ReservedFieldNames = new(StringComparer.OrdinalIgnoreCase)
	{
		StandardMeta.id.data,
		"rom"
	};

	public static VmStructSchema BuildPublicMintSchema(VmStructSchema nftRomSchema)
	{
		return new VmStructSchema
		{
			fields = (nftRomSchema.fields ?? Array.Empty<VmNamedVariableSchema>())
				.Where(field => !ReservedFieldNames.Contains(field.name.data))
				.ToArray(),
			flags = nftRomSchema.flags
		};
	}

	public static byte[] BuildAndSerialize(
		VmStructSchema nftRomSchema,
		IReadOnlyList<MetadataField> metadata)
	{
		if (metadata == null)
		{
			throw new ArgumentNullException(nameof(metadata));
		}

		var reservedField = metadata.FirstOrDefault(field => ReservedFieldNames.Contains(field.Name));
		if (reservedField != null)
		{
			throw new ArgumentException(
				$"Metadata field '{reservedField.Name}' is reserved for chain-owned deterministic mint fields",
				nameof(metadata));
		}

		var publicMintSchema = BuildPublicMintSchema(nftRomSchema);
		var fields = new List<VmNamedDynamicVariable>();
		foreach (var schemaField in publicMintSchema.fields ?? Array.Empty<VmNamedVariableSchema>())
		{
			MetadataHelper.PushMetadataField(schemaField, fields, metadata);
		}

		using MemoryStream romBuffer = new();
		using BinaryWriter writer = new(romBuffer);
		new VmDynamicStruct
		{
			fields = fields.ToArray()
		}.Write(publicMintSchema, writer);

		return romBuffer.ToArray();
	}
}
