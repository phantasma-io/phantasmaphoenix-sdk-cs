namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct TokenCreateData
{
	public string Symbol;
	public string MaxSupply;
	public uint Decimals;
	public bool IsNonFungible;
	public UInt64 CarbonTokenId;
	public Dictionary<string, string> Metadata;

	public TokenCreateData(string symbol, string maxSupply, uint decimals, bool isNonFungible, UInt64 carbonTokenId, Dictionary<string, string> metadata)
	{
		Symbol = symbol;
		MaxSupply = maxSupply;
		Decimals = decimals;
		IsNonFungible = isNonFungible;
		CarbonTokenId = carbonTokenId;
		Metadata = metadata;
	}
}
