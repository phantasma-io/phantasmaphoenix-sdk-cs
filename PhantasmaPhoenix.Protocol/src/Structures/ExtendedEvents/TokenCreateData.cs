namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct TokenCreateData
{
	public readonly string Symbol;
	public readonly string MaxSupply;
	public readonly uint Decimals;
	public readonly bool IsNonFungible;
	public readonly UInt64 CarbonTokenId;
	public readonly Dictionary<string, string> Metadata;

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
