using System;
using System.IO;
using PhantasmaPhoenix.Protocol.Carbon;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

[Flags]
public enum MarketConfigFlags : uint
{
	None = 0,
	PriceRequired = 1u << 0,
	EnforceRoyalties = 1u << 1,
	CanCancelEarly = 1u << 2,
	CanPurchaseLate = 1u << 3
}

public static class MarketConfigDefaults
{
	// On-chain default values (milliseconds).
	public const ulong MinimumListingTimeMs = 1000UL;
	public const ulong MaximumListingTimeMs = 1000UL * 60 * 60 * 24 * 90;
	public const ulong DelistingGraceMs = 1000UL * 60 * 60 * 24;
	public const MarketConfigFlags Flags = MarketConfigFlags.PriceRequired | MarketConfigFlags.EnforceRoyalties;
}

public static class MarketRoyalties
{
	// Percent scale (1% = 10_000_000).
	public const ulong OnePercent = 10000000UL;
	public const ulong HundredPercent = 100UL * OnePercent;
}

public struct MarketConfig : ICarbonBlob
{
	public const int SerializedSize = 28;

	public ulong minimumListingTime;
	public ulong maximumListingTime;
	public ulong delistingGrace;
	public MarketConfigFlags flags;

	public void Write(BinaryWriter w)
	{
		w.Write8(minimumListingTime);
		w.Write8(maximumListingTime);
		w.Write8(delistingGrace);
		w.Write4((uint)flags);
	}

	public void Read(BinaryReader r)
	{
		r.Read8(out minimumListingTime);
		r.Read8(out maximumListingTime);
		r.Read8(out delistingGrace);
		r.Read4(out uint rawFlags);
		flags = (MarketConfigFlags)rawFlags;
	}
}
