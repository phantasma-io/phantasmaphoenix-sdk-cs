using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenSeriesMetadataBuilder
{
	public static byte[] BuildAndSerialize(
		VmStructSchema seriesMetadataSchema,
		BigInteger newPhantasmaSeriesId,
		IReadOnlyList<MetadataField> metadata)
	{
		if (metadata == null)
		{
			throw new ArgumentNullException(nameof(metadata));
		}

		var sharedRom = MetadataHelper.GetOptionalBytesField(metadata, "rom");

		var fields = new List<VmNamedDynamicVariable>
		{
			new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(newPhantasmaSeriesId) },
			new VmNamedDynamicVariable{ name = new SmallString("mode"), value = new VmDynamicVariable((byte)(sharedRom.Length == 0 ? 0 : 1)) },
			new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(sharedRom) },
		};

		var defaultFieldNames = new HashSet<string>(
			MetadataHelper.SeriesDefaultMetadataFields.Select(f => f.Name),
			StringComparer.Ordinal);

		foreach (var schemaField in seriesMetadataSchema.fields ?? Array.Empty<VmNamedVariableSchema>())
		{
			if (defaultFieldNames.Contains(schemaField.name.data))
			{
				continue;
			}

			MetadataHelper.PushMetadataField(schemaField, fields, metadata);
		}

		using MemoryStream metadataBuffer = new();
		using BinaryWriter wMetadata = new(metadataBuffer);
		new VmDynamicStruct
		{
			fields = fields.ToArray()
		}.Write(seriesMetadataSchema, wMetadata);

		return metadataBuffer.ToArray();
	}

}
