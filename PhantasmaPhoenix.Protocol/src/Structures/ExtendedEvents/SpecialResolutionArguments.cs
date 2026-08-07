using System.Collections.Generic;
using Newtonsoft.Json;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

/// <summary>
/// Decoded arguments of one call inside a special resolution.
/// </summary>
/// <remarks>
/// The concrete type is chosen by the module and method of the call that carries it, so a consumer
/// pattern-matches instead of walking an untyped tree:
/// <code>
/// if (call.Arguments is ImportContractsArguments imported)
///     foreach (var contract in imported.Contracts) { /* contract.Script, contract.Tables ... */ }
/// </code>
/// A call whose method this build does not decode carries <see cref="RawArguments"/> instead, holding
/// the argument buffer verbatim - never a partially decoded object, which a consumer could not tell
/// apart from a fully decoded one.
///
/// Every numeric field is a string: chain values are big integers, and JSON numbers silently lose
/// precision above 2^53 in JavaScript consumers.
///
/// Field types across this whole family follow one rule, so a consumer can predict them without
/// reading the decoder:
/// <list type="bullet">
/// <item>an opaque byte payload - a script, an ABI, a ROM/RAM blob, a metadata or schema blob, an
/// undecoded argument buffer - is <see cref="PhantasmaPhoenix.Core.HexBytes"/>, so it can be used as
/// bytes without a round trip through text (the wire is the same hex string it always was);</item>
/// <item>an identifier keeps its canonical text form - an address stays base58, a series or instance
/// id stays a decimal string, a node id stays hex text - because callers match, key and display those
/// values rather than decode them;</item>
/// <item>a value whose type the VM decides at runtime is <see cref="Protocol.VmValue"/>, never bytes.</item>
/// </list>
/// </remarks>
public abstract class SpecialResolutionArguments
{
}

/// <summary>
/// Arguments of a call this build cannot decode: the raw argument buffer as hex.
/// </summary>
public sealed class RawArguments : SpecialResolutionArguments
{
	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes RawArgs { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());
}

/// <summary>
/// A key/value row of contract storage, both sides hex-encoded because they hold arbitrary bytes.
/// </summary>
public sealed class ContractStorageRow
{
	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Key { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Value { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());
}

/// <summary>
/// One map or list table of a contract, with every row it carries.
/// </summary>
public sealed class ContractStorageTable
{
	public string Name { get; set; } = string.Empty;

	public ContractStorageRow[] Rows { get; set; } = System.Array.Empty<ContractStorageRow>();
}

/// <summary>
/// One contract restored by a migration: identity, code and the whole of its stored state.
/// </summary>
public sealed class ImportedContract
{
	public string Name { get; set; } = string.Empty;

	public string Address { get; set; } = string.Empty;

	public string Owner { get; set; } = string.Empty;

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Script { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Abi { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	/// <summary>Root-level contract variables.</summary>
	public ContractStorageRow[] RootVariables { get; set; } = System.Array.Empty<ContractStorageRow>();

	/// <summary>Map and list tables, including their backing rows.</summary>
	public ContractStorageTable[] Tables { get; set; } = System.Array.Empty<ContractStorageTable>();
}

public sealed class ImportContractsArguments : SpecialResolutionArguments
{
	public string ContractsCount { get; set; } = "0";

	public ImportedContract[] Contracts { get; set; } = System.Array.Empty<ImportedContract>();
}

/// <summary>
/// Definition needed to rebuild one Phantasma series.
/// </summary>
public sealed class SeriesSupplement
{
	public string Token { get; set; } = string.Empty;

	public string TokenId { get; set; } = string.Empty;

	public string PhantasmaSeriesId { get; set; } = string.Empty;

	public string MaxSupply { get; set; } = string.Empty;

	public string MintCount { get; set; } = string.Empty;

	public string Mode { get; set; } = string.Empty;

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Script { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Abi { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Rom { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());
}

/// <summary>
/// Mint-count repair of one Phantasma series.
/// </summary>
public sealed class SeriesMintCountRepair
{
	public string Token { get; set; } = string.Empty;

	public string TokenId { get; set; } = string.Empty;

	public string PhantasmaSeriesId { get; set; } = string.Empty;

	public string ImportedLiveCount { get; set; } = string.Empty;

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Script { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Abi { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());
}

public sealed class RepairSeriesArguments : SpecialResolutionArguments
{
	public string SupplementsCount { get; set; } = "0";

	public SeriesSupplement[] Supplements { get; set; } = System.Array.Empty<SeriesSupplement>();

	public string RepairsCount { get; set; } = "0";

	public SeriesMintCountRepair[] Repairs { get; set; } = System.Array.Empty<SeriesMintCountRepair>();
}

/// <summary>
/// Repair of one token definition.
/// </summary>
public sealed class TokenRepair
{
	public string Token { get; set; } = string.Empty;

	public string TokenId { get; set; } = string.Empty;

	public string Symbol { get; set; } = string.Empty;

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Script { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	[JsonConverter(typeof(HexBytesJsonConverter))]
	public HexBytes Abi { get; set; } = HexBytes.FromBytes(System.Array.Empty<byte>());

	public string TokenFlags { get; set; } = string.Empty;

	/// <summary>
	/// Bitmask of the repair operations the chain was asked to perform. Kept numeric on purpose: a new
	/// chain-side operation must not silently render as an unrelated name here.
	/// </summary>
	public string RepairMask { get; set; } = string.Empty;
}

public sealed class RepairTokenArguments : SpecialResolutionArguments
{
	public string RepairsCount { get; set; } = "0";

	public TokenRepair[] Repairs { get; set; } = System.Array.Empty<TokenRepair>();
}
