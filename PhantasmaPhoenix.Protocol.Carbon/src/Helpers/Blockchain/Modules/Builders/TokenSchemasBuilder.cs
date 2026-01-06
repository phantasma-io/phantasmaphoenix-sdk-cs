using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenSchemasBuilder
{
	public static TokenSchemas PrepareStandardTokenSchemas(bool sharedMetadata = false)
	{
		TokenSchemas tokenSchemas = new TokenSchemas
		{
			// Token's series metadata schema
			seriesMetadata = DefaultSeriesSchema(sharedMetadata),
			// NFT's ROM schema
			rom = DefaultNftRomSchema(sharedMetadata),
			// NFT's RAM schema
			ram = new VmStructSchema { fields = Array.Empty<VmNamedVariableSchema>(), flags = VmStructSchema.Flags.DynamicExtras }
		};

		return tokenSchemas;
	}

	public static TokenSchemasJson ParseTokenSchemasJson(string json)
	{
		var raw = JObject.Parse(json);

		return new TokenSchemasJson
		{
			SeriesMetadata = ParseFieldArray(raw, "seriesMetadata"),
			Rom = ParseFieldArray(raw, "rom"),
			Ram = ParseFieldArray(raw, "ram")
		};
	}

	public static TokenSchemas FromJson(string json)
	{
		var tokenSchemasJson = ParseTokenSchemasJson(json);

		var tokenSchemas = new TokenSchemas
		{
			seriesMetadata = SeriesSchemaFromFieldTypes(tokenSchemasJson.SeriesMetadata),
			rom = NftRomSchemaFromFieldTypes(tokenSchemasJson.Rom),
			ram = NftRamSchemaFromFieldTypes(tokenSchemasJson.Ram)
		};

		var (ok, error) = Verify(tokenSchemas);
		if (!ok)
		{
			throw new ArgumentException(error ?? "Unknown error");
		}

		return tokenSchemas;
	}

	public static (bool ok, string? error) Verify(TokenSchemas schemas)
	{
		var result = AssertMetadataField(
			new[] { schemas.seriesMetadata, schemas.rom },
			MetadataHelper.StandardMetadataFields);
		if (!result.ok)
		{
			return result;
		}

		result = AssertMetadataField(
			new[] { schemas.seriesMetadata },
			MetadataHelper.SeriesDefaultMetadataFields);
		if (!result.ok)
		{
			return result;
		}

		result = AssertMetadataField(
			new[] { schemas.rom },
			MetadataHelper.NftDefaultMetadataFields);
		if (!result.ok)
		{
			return result;
		}

		return (true, null);
	}

	public static byte[] Serialize(TokenSchemas tokenSchemas)
	{
		using MemoryStream schemaBuffer = new();
		using BinaryWriter wSchemas = new(schemaBuffer);
		wSchemas.Write(tokenSchemas);
		return schemaBuffer.ToArray();
	}

	public static string SerializeHex(TokenSchemas tokenSchemas)
	{
		return Serialize(tokenSchemas).ToHex();
	}

	public static byte[] BuildAndSerialize(TokenSchemas? tokenSchemas)
	{
		return Serialize(tokenSchemas ?? PrepareStandardTokenSchemas());
	}

	private static VmStructSchema DefaultSeriesSchema(bool sharedMetadata)
	{
		var fields = new List<VmNamedVariableSchema>();

		foreach (var fieldType in MetadataHelper.SeriesDefaultMetadataFields)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		if (sharedMetadata)
		{
			foreach (var fieldType in MetadataHelper.StandardMetadataFields)
			{
				fields.Add(new VmNamedVariableSchema
				{
					name = new SmallString(fieldType.Name),
					schema = new VmVariableSchema { type = fieldType.Type }
				});
			}
		}

		return new VmStructSchema
		{
			fields = fields.ToArray(),
			flags = VmStructSchema.Flags.None
		};
	}

	private static VmStructSchema DefaultNftRomSchema(bool sharedMetadata)
	{
		var fields = new List<VmNamedVariableSchema>();

		foreach (var fieldType in MetadataHelper.NftDefaultMetadataFields)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		if (!sharedMetadata)
		{
			foreach (var fieldType in MetadataHelper.StandardMetadataFields)
			{
				fields.Add(new VmNamedVariableSchema
				{
					name = new SmallString(fieldType.Name),
					schema = new VmVariableSchema { type = fieldType.Type }
				});
			}
		}

		return new VmStructSchema
		{
			fields = fields.ToArray(),
			flags = VmStructSchema.Flags.None
		};
	}

	private static VmStructSchema SeriesSchemaFromFieldTypes(IReadOnlyList<FieldType> fieldTypes)
	{
		var fields = new List<VmNamedVariableSchema>();

		foreach (var fieldType in MetadataHelper.SeriesDefaultMetadataFields)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		foreach (var fieldType in fieldTypes)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		return new VmStructSchema
		{
			fields = fields.ToArray(),
			flags = VmStructSchema.Flags.None
		};
	}

	private static VmStructSchema NftRomSchemaFromFieldTypes(IReadOnlyList<FieldType> fieldTypes)
	{
		var fields = new List<VmNamedVariableSchema>();

		foreach (var fieldType in MetadataHelper.NftDefaultMetadataFields)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		foreach (var fieldType in fieldTypes)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		return new VmStructSchema
		{
			fields = fields.ToArray(),
			flags = VmStructSchema.Flags.None
		};
	}

	private static VmStructSchema NftRamSchemaFromFieldTypes(IReadOnlyList<FieldType> fieldTypes)
	{
		if (fieldTypes.Count == 0)
		{
			return new VmStructSchema
			{
				fields = Array.Empty<VmNamedVariableSchema>(),
				flags = VmStructSchema.Flags.DynamicExtras
			};
		}

		var fields = new List<VmNamedVariableSchema>();
		foreach (var fieldType in fieldTypes)
		{
			fields.Add(new VmNamedVariableSchema
			{
				name = new SmallString(fieldType.Name),
				schema = new VmVariableSchema { type = fieldType.Type }
			});
		}

		return new VmStructSchema
		{
			fields = fields.ToArray(),
			flags = VmStructSchema.Flags.None
		};
	}

	private static (bool ok, string? error) AssertMetadataField(
		IReadOnlyList<VmStructSchema> schemas,
		IReadOnlyList<FieldType> fieldTypes)
	{
		foreach (var fieldType in fieldTypes)
		{
			var fieldFound = false;
			foreach (var schema in schemas)
			{
				var schemaFields = schema.fields ?? Array.Empty<VmNamedVariableSchema>();
				var index = Array.FindIndex(schemaFields, f => f.name.data == fieldType.Name);
				if (index >= 0)
				{
					if (schemaFields[index].schema.type != fieldType.Type)
					{
						return (false,
							$"Type mismatch for {fieldType.Name} field, must be {schemaFields[index].schema.type} instead of {fieldType.Type}");
					}
					fieldFound = true;
					break;
				}

				var caseIndex = Array.FindIndex(
					schemaFields,
					f => string.Equals(f.name.data, fieldType.Name, StringComparison.OrdinalIgnoreCase));
				if (caseIndex >= 0)
				{
					return (false,
						$"Case mismatch for {fieldType.Name} field, must be {schemaFields[caseIndex].name.data}");
				}
			}

			if (!fieldFound)
			{
				return (false, $"Mandatory metadata field not found: {fieldType.Name}");
			}
		}

		return (true, null);
	}

	private static List<FieldType> ParseFieldArray(JObject raw, string key)
	{
		if (!raw.TryGetValue(key, out var token) || token is not JArray arr)
		{
			throw new ArgumentException($"{key} must be an array");
		}

		var fields = new List<FieldType>();
		foreach (var item in arr)
		{
			if (item is not JObject obj)
			{
				throw new ArgumentException($"{key} field name must be string");
			}

			if (!obj.TryGetValue("name", out var nameToken) || nameToken.Type != JTokenType.String)
			{
				throw new ArgumentException($"{key} field name must be string");
			}

			if (!obj.TryGetValue("type", out var typeToken) || typeToken.Type != JTokenType.String)
			{
				throw new ArgumentException($"{key} field type must be string");
			}

			var name = nameToken.Value<string>() ?? string.Empty;
			var type = typeToken.Value<string>() ?? string.Empty;
			fields.Add(new FieldType(name, VmTypeFromString(type)));
		}

		return fields;
	}

	private static VmType VmTypeFromString(string type)
	{
		var trimmed = type.Trim();
		if (!VmTypeMap.TryGetValue(trimmed, out var value))
		{
			throw new ArgumentException($"Unknown VmType: {type}");
		}

		return value;
	}

	private static readonly Dictionary<string, VmType> VmTypeMap = new(StringComparer.Ordinal)
	{
		["Dynamic"] = VmType.Dynamic,
		["Array"] = VmType.Array,
		["Bytes"] = VmType.Bytes,
		["Struct"] = VmType.Struct,
		["Int8"] = VmType.Int8,
		["Int16"] = VmType.Int16,
		["Int32"] = VmType.Int32,
		["Int64"] = VmType.Int64,
		["Int256"] = VmType.Int256,
		["Bytes16"] = VmType.Bytes16,
		["Bytes32"] = VmType.Bytes32,
		["Bytes64"] = VmType.Bytes64,
		["String"] = VmType.String,
		["Array_Dynamic"] = VmType.Array_Dynamic,
		["Array_Bytes"] = VmType.Array_Bytes,
		["Array_Struct"] = VmType.Array_Struct,
		["Array_Int8"] = VmType.Array_Int8,
		["Array_Int16"] = VmType.Array_Int16,
		["Array_Int32"] = VmType.Array_Int32,
		["Array_Int64"] = VmType.Array_Int64,
		["Array_Int256"] = VmType.Array_Int256,
		["Array_Bytes16"] = VmType.Array_Bytes16,
		["Array_Bytes32"] = VmType.Array_Bytes32,
		["Array_Bytes64"] = VmType.Array_Bytes64,
		["Array_String"] = VmType.Array_String
	};
}

public sealed class TokenSchemasJson
{
	public IReadOnlyList<FieldType> SeriesMetadata { get; set; } = Array.Empty<FieldType>();
	public IReadOnlyList<FieldType> Rom { get; set; } = Array.Empty<FieldType>();
	public IReadOnlyList<FieldType> Ram { get; set; } = Array.Empty<FieldType>();
}
