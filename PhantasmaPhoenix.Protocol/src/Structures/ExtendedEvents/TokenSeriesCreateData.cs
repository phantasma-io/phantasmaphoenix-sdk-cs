namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct TokenSeriesCreateData
{
	public readonly uint MaxMint;
	public readonly uint MaxSupply;
	public readonly string Owner;
	public readonly UInt64 CarbonTokenId;
	public readonly UInt32 CarbonSeriesId;
	public readonly Dictionary<string, string> Metadata;

	public TokenSeriesCreateData(uint maxMint, uint maxSupply, string owner, UInt64 carbonTokenId, UInt32 carbonSeriesId, Dictionary<string, string> metadata)
	{
		MaxMint = maxMint;
		MaxSupply = maxSupply;
		Owner = owner;
		CarbonTokenId = carbonTokenId;
		CarbonSeriesId = carbonSeriesId;
		Metadata = metadata;
	}
}
