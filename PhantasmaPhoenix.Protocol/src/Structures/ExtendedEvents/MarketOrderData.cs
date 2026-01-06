namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct MarketOrderData
{
	public string BaseSymbol;
	public string QuoteSymbol;
	public string TokenId;
	public ulong CarbonBaseTokenId;
	public ulong CarbonQuoteTokenId;
	public ulong CarbonInstanceId;
	public string Seller;
	public string Buyer;
	public string Price;
	public string EndPrice;
	public long StartDate;
	public long EndDate;
	public string Type;

	public MarketOrderData(
		string baseSymbol,
		string quoteSymbol,
		string tokenId,
		ulong carbonBaseTokenId,
		ulong carbonQuoteTokenId,
		ulong carbonInstanceId,
		string seller,
		string buyer,
		string price,
		string endPrice,
		long startDate,
		long endDate,
		string type)
	{
		BaseSymbol = baseSymbol;
		QuoteSymbol = quoteSymbol;
		TokenId = tokenId;
		CarbonBaseTokenId = carbonBaseTokenId;
		CarbonQuoteTokenId = carbonQuoteTokenId;
		CarbonInstanceId = carbonInstanceId;
		Seller = seller;
		Buyer = buyer;
		Price = price;
		EndPrice = endPrice;
		StartDate = startDate;
		EndDate = endDate;
		Type = type;
	}
}
