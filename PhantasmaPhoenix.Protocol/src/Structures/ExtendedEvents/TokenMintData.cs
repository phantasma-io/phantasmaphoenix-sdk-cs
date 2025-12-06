using System.Collections.Generic;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct TokenMintData
{
	public string Symbol;
	public string TokenId;
	public string SeriesId;
	public uint MintNumber;
	public ulong CarbonTokenId;
	public uint CarbonSeriesId;
	public ulong CarbonInstanceId;
	public string Owner;
	public Dictionary<string, string> Metadata;

	public TokenMintData(string symbol, string tokenId, string seriesId, uint mintNumber, ulong carbonTokenId,
		uint carbonSeriesId, ulong carbonInstanceId, string owner, Dictionary<string, string> metadata)
	{
		Symbol = symbol;
		TokenId = tokenId;
		SeriesId = seriesId;
		MintNumber = mintNumber;
		CarbonTokenId = carbonTokenId;
		CarbonSeriesId = carbonSeriesId;
		CarbonInstanceId = carbonInstanceId;
		Owner = owner;
		Metadata = metadata;
	}
}
