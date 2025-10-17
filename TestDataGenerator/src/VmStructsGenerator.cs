using Newtonsoft.Json;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public static partial class VmStructsGenerator
{
	public static string VmStruct1Generate()
	{
		// Create a carbon structure to describe the schema for these tokens -- i.e. the variables our NFTs will have
		var tokenSchemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		using MemoryStream schemaBuffer = new();
		using BinaryWriter wSchemas = new(schemaBuffer);
		wSchemas.Write(tokenSchemas);

		return schemaBuffer.ToArray().ToHex();
	}

	public static string VmStruct2Generate()
	{
		var fieldsJson = "{\"name\": \"My test token!\", \"url\": \"http://example.com\"}";

		Dictionary<string, string>? fields =
			JsonConvert.DeserializeObject<Dictionary<string, string>>(
				fieldsJson
			);
		if (fields == null || fields.Count == 0)
		{
			throw new("Could not deserialize fields");
		}

		VmNamedDynamicVariable[] metadataFields = [];
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

		return metadataBuffer.ToArray().ToHex();
	}
}
