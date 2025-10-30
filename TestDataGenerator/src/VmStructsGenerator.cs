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
		var fieldsJson = "{\"name\":\"My test token!\",\"icon\":\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E%3Cpath fill='%23F44336' d='M7 4h5a5 5 0 010 10H9v6H7zM9 6v6h3a3 3 0 000-6z'/%3E%3C/svg%3E\",\"url\":\"http://example.com\",\"description\":\"My test token description\"}";

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
