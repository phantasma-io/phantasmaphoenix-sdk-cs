namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

public enum ListingType : byte
{
	FixedPrice
}

public struct TokenListing : ICarbonBlob
{
	public ListingType type;
	public Bytes32 seller;
	public UInt64 quoteTokenId;
	public IntX price;
	public Int64 startDate;
	public Int64 endDate;

	public void Write(BinaryWriter w)
	{
		w.Write1(type);
		w.Write32(seller);
		w.Write8(quoteTokenId);
		w.Write(price);
		w.Write8(startDate);
		w.Write8(endDate);
	}

	public void Read(BinaryReader r)
	{
		r.Read1(out type);
		r.Read32(out seller);
		r.Read8(out quoteTokenId);
		r.Read(out price);
		r.Read8(out startDate);
		r.Read8(out endDate);
	}
}
