using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenSchemasBuilder
{
	public static TokenSchemas PrepareStandardTokenSchemas()
	{
		TokenSchemas tokenSchemas = new TokenSchemas
		{
			// Variables that every new NFT-series must supply:
			seriesMetadata = new VmStructSchema
			{
				// Every series must have: BigInteger ID; Byte mode; Byte[] phantasmaRom;
				fields = new[] {
					new VmNamedVariableSchema {
						name = StandardMeta.id,
						schema = new VmVariableSchema{ type = VmType.Int256 }
					},
					new VmNamedVariableSchema { // Phantasma feature: Unique or duplicate series type
						name = new SmallString("mode"),
						schema = new VmVariableSchema{ type = VmType.Int8 }
					},
					new VmNamedVariableSchema { // Phantasma feature: If this is a duplicated series, store the duplicated ROM here:
						name = new SmallString("rom"),
						schema = new VmVariableSchema{ type = VmType.Bytes }
					}
				},
				flags = VmStructSchema.Flags.None
			},

			// Variables that every mint must supply:
			rom = new VmStructSchema
			{
				// Every NFT must have: Int256 ID; Byte[] phantasmaRom;
				fields = new[] {
					new VmNamedVariableSchema {
						name = StandardMeta.id,
						schema = new VmVariableSchema{ type = VmType.Int256 }
					},
					new VmNamedVariableSchema { // Phantasma feature: If this is NOT a duplicated series, store the individual ROM here:
						name = new SmallString("rom"),
						schema = new VmVariableSchema{ type = VmType.Bytes }
					}
				},
				flags = VmStructSchema.Flags.None
			},

			// Variables that can be updated after minting:
			ram = new VmStructSchema { fields = Array.Empty<VmNamedVariableSchema>(), flags = VmStructSchema.Flags.DynamicExtras },
			// ^ Leave this as dynamic so users can put anything there
		};

		return tokenSchemas;
	}

	public static byte[] BuildAndSerialize(TokenSchemas? tokenSchemas)
	{
		// Create a carbon structure to describe the schema for these tokens - i.e. the attributes our NFTs will have
		using MemoryStream schemaBuffer = new();
		using BinaryWriter wSchemas = new(schemaBuffer);
		wSchemas.Write(tokenSchemas ?? PrepareStandardTokenSchemas());
		return schemaBuffer.ToArray();
	}
}
