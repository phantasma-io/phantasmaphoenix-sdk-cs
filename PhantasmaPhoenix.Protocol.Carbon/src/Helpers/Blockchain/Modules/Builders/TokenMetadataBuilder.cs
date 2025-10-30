using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenMetadataBuilder
{
	public static byte[] BuildAndSerialize(Dictionary<string, string> fields)
	{
		var requiredFields = new[] { "name", "icon", "url", "description" };

		if (fields == null || fields.Count < requiredFields.Length)
		{
			throw new ArgumentException("Token metadata is mandatory", nameof(fields));
		}

		var missing = requiredFields
			.Where(field => !fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
			.ToArray();

		if (missing.Length > 0)
		{
			throw new ArgumentException(
				$"Token metadata is missing required fields: {string.Join(", ", missing)}",
				nameof(fields));
		}

		var metadataFields = fields
			.Select(f => new VmNamedDynamicVariable
			{
				name = new SmallString(f.Key),
				value = new VmDynamicVariable(f.Value)
			})
			.ToArray();

		// Create a carbon structure for the token metadata
		using MemoryStream metadataBuffer = new();
		using BinaryWriter wMetadata = new(metadataBuffer);
		wMetadata.Write(new VmDynamicStruct
		{
			fields = metadataFields
		});

		return metadataBuffer.ToArray();
	}
}
