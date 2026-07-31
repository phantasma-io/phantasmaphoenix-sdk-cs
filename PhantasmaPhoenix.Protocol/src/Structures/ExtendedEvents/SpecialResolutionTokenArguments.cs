using System.Collections.Generic;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

// Arguments of the token module calls a special resolution can carry. Shapes that repeat across
// methods share one type on purpose: a query by token id looks the same whichever query it is.

/// <summary>Token identity: the resolved symbol plus the numeric id it was resolved from.</summary>
public class TokenReferenceArguments : SpecialResolutionArguments
{
	public string Token { get; set; } = string.Empty;

	public string TokenId { get; set; } = string.Empty;
}

public sealed class TokenSeriesReferenceArguments : TokenReferenceArguments
{
	public string SeriesId { get; set; } = string.Empty;
}

public sealed class SymbolArguments : SpecialResolutionArguments
{
	public string Symbol { get; set; } = string.Empty;
}

public sealed class TransferFungibleArguments : TokenReferenceArguments
{
	public string From { get; set; } = string.Empty;

	public string To { get; set; } = string.Empty;

	public string Amount { get; set; } = string.Empty;
}

public sealed class TransferNonFungibleArguments : TokenReferenceArguments
{
	public string From { get; set; } = string.Empty;

	public string To { get; set; } = string.Empty;

	public string[] InstanceIds { get; set; } = System.Array.Empty<string>();
}

public sealed class MintFungibleArguments : TokenReferenceArguments
{
	public string To { get; set; } = string.Empty;

	public string Amount { get; set; } = string.Empty;
}

public sealed class BurnFungibleArguments : TokenReferenceArguments
{
	public string From { get; set; } = string.Empty;

	public string Amount { get; set; } = string.Empty;
}

public sealed class BalanceArguments : TokenReferenceArguments
{
	public string Address { get; set; } = string.Empty;
}

public sealed class CreateTokenArguments : SpecialResolutionArguments
{
	public string Symbol { get; set; } = string.Empty;

	public string Owner { get; set; } = string.Empty;

	public string MaxSupply { get; set; } = string.Empty;

	public string Decimals { get; set; } = string.Empty;

	public string Flags { get; set; } = string.Empty;

	/// <summary>Decoded metadata fields; absent when the token carries none.</summary>
	public Dictionary<string, string>? Metadata { get; set; }

	/// <summary>NFT schema blob as hex; absent for fungible tokens.</summary>
	public string? TokenSchemas { get; set; }
}

/// <summary>Series definition, as carried by a series-creating call.</summary>
public class TokenSeriesArguments : TokenReferenceArguments
{
	public string Owner { get; set; } = string.Empty;

	public string MaxMint { get; set; } = string.Empty;

	public string MaxSupply { get; set; } = string.Empty;

	/// <summary>Decoded series metadata; absent when the token declares no schema for it.</summary>
	public Dictionary<string, string>? Metadata { get; set; }

	/// <summary>Phantasma series id taken from the decoded metadata, when the schema carries one.</summary>
	public string? SeriesId { get; set; }

	/// <summary>Metadata blob as hex, reported instead of <see cref="Metadata"/> when it cannot be decoded.</summary>
	public string? MetadataRaw { get; set; }
}

public sealed class CreateMintedTokenSeriesArguments : TokenSeriesArguments
{
	public string Recipient { get; set; } = string.Empty;

	public string[] Roms { get; set; } = System.Array.Empty<string>();

	public string[] Rams { get; set; } = System.Array.Empty<string>();
}

/// <summary>One NFT to mint, addressed by the carbon series id.</summary>
public sealed class NftMint
{
	public string SeriesId { get; set; } = string.Empty;

	public string Rom { get; set; } = string.Empty;

	public string Ram { get; set; } = string.Empty;
}

/// <summary>One NFT to mint, addressed by the 32-byte Phantasma series id.</summary>
public sealed class PhantasmaNftMint
{
	public string PhantasmaSeriesId { get; set; } = string.Empty;

	public string Rom { get; set; } = string.Empty;

	public string Ram { get; set; } = string.Empty;
}

public sealed class MintNonFungibleArguments : TokenReferenceArguments
{
	public string Owner { get; set; } = string.Empty;

	public NftMint[] Tokens { get; set; } = System.Array.Empty<NftMint>();
}

public sealed class MintPhantasmaNonFungibleArguments : TokenReferenceArguments
{
	public string Owner { get; set; } = string.Empty;

	public PhantasmaNftMint[] Tokens { get; set; } = System.Array.Empty<PhantasmaNftMint>();
}

public sealed class BurnNonFungibleArguments : TokenReferenceArguments
{
	public string Address { get; set; } = string.Empty;

	public string[] InstanceIds { get; set; } = System.Array.Empty<string>();
}

public sealed class NonFungibleInfoArguments : TokenReferenceArguments
{
	public string InstanceId { get; set; } = string.Empty;

	public string GetSchemas { get; set; } = string.Empty;
}

public sealed class NonFungibleInfoByRomIdArguments : TokenReferenceArguments
{
	public string RomId { get; set; } = string.Empty;

	public string GetSchemas { get; set; } = string.Empty;
}

public sealed class SeriesInfoByMetaIdArguments : TokenReferenceArguments
{
	public string RomId { get; set; } = string.Empty;
}

public sealed class TokensConfigArguments : SpecialResolutionArguments
{
	public string Flags { get; set; } = string.Empty;

	/// <summary>Names of the flags that are set, including a Reserved0xNN entry for unknown bits.</summary>
	public string[] FlagsNames { get; set; } = System.Array.Empty<string>();
}

public sealed class UpdateTokenMetadataArguments : TokenReferenceArguments
{
	public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class UpdateSeriesMetadataArguments : TokenReferenceArguments
{
	public string SeriesId { get; set; } = string.Empty;

	/// <summary>Metadata blob as hex: this call carries it unschematized.</summary>
	public string Metadata { get; set; } = string.Empty;
}
