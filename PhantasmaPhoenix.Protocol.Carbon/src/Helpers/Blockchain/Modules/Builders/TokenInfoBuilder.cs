namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static class TokenInfoBuilder
{
	public static TokenInfo Build(string symbol, IntX maxSupply, bool isNFT, uint decimals, Bytes32 creatorPublicKey, byte[]? metadata = null, byte[]? tokenSchemas = null)
	{
		return new TokenInfo
		{
			maxSupply = maxSupply,
			flags = isNFT ? TokenFlags.NonFungible : TokenFlags.BigFungible,
			decimals = decimals,
			owner = creatorPublicKey,
			symbol = new SmallString(symbol),
			metadata = metadata ?? TokenMetadataBuilder.BuildAndSerialize(null),
			tokenSchemas = tokenSchemas ?? TokenSchemasBuilder.BuildAndSerialize(null)
		};
	}
}
