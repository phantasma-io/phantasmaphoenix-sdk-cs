using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenMetadataBuilder
{
	public static byte[] BuildAndSerialize(Dictionary<string, string>? fields)
	{
		fields ??= new Dictionary<string, string>();

		var metadataFields = new VmNamedDynamicVariable[] { };
		foreach (var f in fields)
		{
			metadataFields = metadataFields.Append(new VmNamedDynamicVariable
			{
				name = new SmallString(f.Key),
				value = new VmDynamicVariable(f.Value)
			}).ToArray();
		}

		// Create a carbon structure for the token metadata
		using MemoryStream metadataBuffer = new();
		using BinaryWriter wMetadata = new(metadataBuffer);
		wMetadata.Write(new VmDynamicStruct
		{
			fields = metadataFields
		});
		// ^ There's no standard for token metadata field names yet!

		return metadataBuffer.ToArray();
	}
}
