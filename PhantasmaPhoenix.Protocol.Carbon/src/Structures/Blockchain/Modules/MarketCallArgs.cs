using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

/*
 * Market contract call arguments for ModuleId.Market. The field order must
 * match the serialized call argument layout.
 */
public struct MarketSellTokenArgs : ICarbonBlob
{
	public Bytes32 from;
	public UInt64 tokenId;
	public UInt64 instanceId;
	public UInt64 quoteTokenId;
	public IntX price;
	public Int64 endDate;

	public void Write(BinaryWriter w)
	{
		w.Write32(from);
		w.Write8(tokenId);
		w.Write8(instanceId);
		w.Write8(quoteTokenId);
		w.Write(price);
		w.Write8(endDate);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out from);
		r.Read8(out tokenId);
		r.Read8(out instanceId);
		r.Read8(out quoteTokenId);
		r.Read(out price);
		r.Read8(out endDate);
	}
}

public struct MarketSellTokenByIdArgs : ICarbonBlob
{
	public Bytes32 from;
	public SmallString symbol;
	public VmDynamicVariable instanceId;
	public SmallString quoteSymbol;
	public IntX price;
	public Int64 endDate;

	public void Write(BinaryWriter w)
	{
		w.Write32(from);
		w.Write(symbol);
		w.Write(instanceId);
		w.Write(quoteSymbol);
		w.Write(price);
		w.Write8(endDate);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out from);
		r.Read(out symbol);
		r.Read(out instanceId);
		r.Read(out quoteSymbol);
		r.Read(out price);
		r.Read8(out endDate);
	}
}

public struct MarketCancelSaleArgs : ICarbonBlob
{
	public UInt64 tokenId;
	public UInt64 instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write8(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read8(out instanceId);
	}
}

public struct MarketCancelSaleByIdArgs : ICarbonBlob
{
	public SmallString symbol;
	public VmDynamicVariable instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write(symbol);
		w.Write(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read(out symbol);
		r.Read(out instanceId);
	}
}

public struct MarketBuyTokenArgs : ICarbonBlob
{
	public Bytes32 from;
	public UInt64 tokenId;
	public UInt64 instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write32(from);
		w.Write8(tokenId);
		w.Write8(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out from);
		r.Read8(out tokenId);
		r.Read8(out instanceId);
	}
}

public struct MarketBuyTokenByIdArgs : ICarbonBlob
{
	public Bytes32 from;
	public SmallString symbol;
	public VmDynamicVariable instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write32(from);
		w.Write(symbol);
		w.Write(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read32(out from);
		r.Read(out symbol);
		r.Read(out instanceId);
	}
}

public struct MarketGetTokenListingCountArgs : ICarbonBlob
{
	public UInt64 tokenId;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
	}
}

public struct MarketGetTokenListingInfoArgs : ICarbonBlob
{
	public UInt64 tokenId;
	public UInt64 instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write8(tokenId);
		w.Write8(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out tokenId);
		r.Read8(out instanceId);
	}
}

public struct MarketGetTokenListingInfoByIdArgs : ICarbonBlob
{
	public SmallString symbol;
	public VmDynamicVariable instanceId;

	public void Write(BinaryWriter w)
	{
		w.Write(symbol);
		w.Write(instanceId);
	}

	public void Read(BinaryReader r)
	{
		r.Read(out symbol);
		r.Read(out instanceId);
	}
}
