using System.Numerics;

namespace PhantasmaPhoenix.Protocol;

public struct MarketEventData
{
	public string BaseSymbol;
	public string QuoteSymbol;
	public BigInteger ID;
	public BigInteger Price;
	public BigInteger EndPrice;
	public TypeAuction Type;
}
