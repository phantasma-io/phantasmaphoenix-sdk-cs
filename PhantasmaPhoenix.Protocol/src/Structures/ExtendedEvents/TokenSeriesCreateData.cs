namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct TokenSeriesCreateData
{
	public string Symbol;
	public string SeriesId;
	public uint MaxMint;
	public uint MaxSupply;
	public string Owner;
	public UInt64 CarbonTokenId;
	public UInt32 CarbonSeriesId;
	public Dictionary<string, string> Metadata;

	public TokenSeriesCreateData(string symbol, string seriesId, uint maxMint, uint maxSupply, string owner, UInt64 carbonTokenId, UInt32 carbonSeriesId, Dictionary<string, string> metadata)
	{
		Symbol = symbol;
		SeriesId = seriesId;
		MaxMint = maxMint;
		MaxSupply = maxSupply;
		Owner = owner;
		CarbonTokenId = carbonTokenId;
		CarbonSeriesId = carbonSeriesId;
		Metadata = metadata;
	}
}
