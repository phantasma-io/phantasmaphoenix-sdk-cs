using System.Numerics;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Native operation kinds supported by the Tier-1 fee estimator.
/// </summary>
public enum NativeFeeKind
{
	/// <summary>Fungible token transfer (native TxTypes.TransferFungible / _GasPayer).</summary>
	TransferFungible,
	/// <summary>NFT transfer of Count instances (TxTypes.TransferNonFungible_*).</summary>
	TransferNonFungible,
	/// <summary>Fungible mint (TxTypes.MintFungible or TokenContract.MintFungible call).</summary>
	MintFungible,
	/// <summary>NFT mint of Count instances (TxTypes.MintNonFungible or contract call).</summary>
	MintNonFungible,
	/// <summary>Fungible burn (TxTypes.BurnFungible / _GasPayer).</summary>
	BurnFungible,
	/// <summary>NFT burn of Count instances (TxTypes.BurnNonFungible / _GasPayer).</summary>
	BurnNonFungible,
	/// <summary>TokenContract.CreateToken call. Requires SymbolLength when a symbol is set.</summary>
	CreateToken,
	/// <summary>TokenContract.CreateTokenSeries call.</summary>
	CreateTokenSeries,
	/// <summary>GovernanceContract.RegisterName call. Requires NameLength.</summary>
	RegisterName,
	/// <summary>
	/// Generic Phantasma VM script transaction (AllowGas/SpendGas pattern: stake, marketplace,
	/// custom contract calls). Script opcode costs are not closed-form in Tier-1; the estimate
	/// budgets ScriptUnitsAllowance VM work units on top of the byte fee. For an exact script
	/// bill use the node-side Tier-2 estimator once available.
	/// </summary>
	Script
}

/// <summary>
/// Optional inputs for <see cref="NativeFeeEstimator.EstimateNativeFee"/>. Defaults produce a
/// safe single-signer estimate; set fields for exactness (the unused part of a gas offer is
/// always refunded, so generous values only lock the balance for the block, they cost nothing).
/// </summary>
public sealed class NativeFeeParams
{
	/// <summary>Instance count for NFT kinds (transferred/minted/burned instances).</summary>
	public uint Count { get; set; } = 1;

	/// <summary>Token symbol length in characters (CreateToken). 0 = no symbol.</summary>
	public int SymbolLength { get; set; }

	/// <summary>Registered name length in characters (RegisterName). Required for that kind.</summary>
	public int NameLength { get; set; }

	/// <summary>
	/// Full signed transaction size in bytes (the envelope carried in the block). 0 = use a
	/// conservative per-kind default. Under gas model v2 every envelope byte is billed, so pass
	/// the real size (serialized message + 96 bytes per witness) for exact numbers.
	/// </summary>
	public uint EnvelopeBytes { get; set; }

	/// <summary>User payload bytes attached to the tx (billed under gas model v1).</summary>
	public uint PayloadBytes { get; set; }

	/// <summary>
	/// Maximum number of paid storage rows the transaction can create (fresh token-balance rows,
	/// NFT lookup rows, ...). -1 = use the per-kind worst-case default. SOUL/KCAL balance rows
	/// are free and never count. Determines MaxData: escrow = rows * dataEscrowPerRow.
	/// </summary>
	public long FreshRows { get; set; } = -1;

	/// <summary>Per-instance ROM+RAM bytes for MintNonFungible (stored state, drives escrow).</summary>
	public uint RomRamBytes { get; set; }

	/// <summary>
	/// VM work-unit allowance for the Script kind. The default (5000) exceeds every script seen
	/// in mainnet history (max 3392 units) with margin.
	/// </summary>
	public ulong ScriptUnitsAllowance { get; set; } = 5000;
}

/// <summary>
/// Result of a Tier-1 fee estimate. All gas values are kcal-base (1 KCAL = 1e10 kcal-base);
/// MaxData is in data-token atoms (SOUL, 1 SOUL = 1e8 atoms).
/// </summary>
public readonly struct NativeFeeEstimate
{
	/// <summary>Recommended gas offer (TxMsg.maxGas). Includes deterministic headroom; the unused part is refunded.</summary>
	public ulong MaxGas { get; }
	/// <summary>Recommended storage-escrow ceiling (TxMsg.maxData). Only the actually created rows are escrowed.</summary>
	public ulong MaxData { get; }
	/// <summary>The bill the chain formula yields for exactly the provided inputs (no headroom).</summary>
	public ulong ExpectedGasBill { get; }

	public NativeFeeEstimate(ulong maxGas, ulong maxData, ulong expectedGasBill)
	{
		MaxGas = maxGas;
		MaxData = maxData;
		ExpectedGasBill = expectedGasBill;
	}
}

/// <summary>
/// Tier-1 static fee calculator: closed-form gas/data offers for native operations, exact under
/// both gas models (selected by GasConfig.version). Mirrors the validator billing formula
/// (node blockchain.cpp settlement + token/governance contract gas sites); any change to those
/// formulas ships as a new gas-model version, never silently.
/// </summary>
public static class NativeFeeEstimator
{
	/// <summary>
	/// Gas model v2 price of block-carried bytes, in gas units per byte. A versioned consensus
	/// constant of the v2 gas model (node data_blockchain.h kGasModelV2UnitsPerBlockDataByte),
	/// deliberately not part of the on-chain config.
	/// </summary>
	public const ulong GasModelV2UnitsPerBlockDataByte = 25;

	/// <summary>Serialized size of one witness-array entry (32-byte address + 64-byte signature).</summary>
	public const uint WitnessArrayEntryBytes = 96;

	/// <summary>Serialized size of one bare signature (native TxTypes carry no witness array).</summary>
	public const uint NativeSignatureBytes = 64;

	/// <summary>
	/// Estimates maxGas/maxData for a native operation under the given on-chain gas config.
	/// </summary>
	/// <param name="kind">Operation kind.</param>
	/// <param name="config">Current chain gas config (see the getGasConfig RPC method).</param>
	/// <param name="p">Optional inputs; null uses safe single-signer defaults.</param>
	public static NativeFeeEstimate EstimateNativeFee(NativeFeeKind kind, in GasConfig config, NativeFeeParams? p = null)
	{
		p ??= new NativeFeeParams();
		if (p.Count == 0)
			throw new ArgumentOutOfRangeException(nameof(p), "Count must be at least 1");

		var count = p.Count;
		// Work units consumed by the operation itself (the ConsumeGas amounts in the token /
		// governance contracts) and, under v2, the direct kcal-base policy fee that replaces the
		// v1 unit-priced product prices.
		ulong workUnits;
		ulong policyFee = 0;
		var v2 = config.HasGasModelV2;
		switch (kind)
		{
			case NativeFeeKind.TransferFungible:
			case NativeFeeKind.MintFungible:
			case NativeFeeKind.BurnFungible:
				workUnits = config.gasFeeTransfer;
				break;
			case NativeFeeKind.TransferNonFungible:
			case NativeFeeKind.MintNonFungible:
			case NativeFeeKind.BurnNonFungible:
				workUnits = SaturatingMul(config.gasFeeTransfer, count);
				break;
			case NativeFeeKind.CreateToken:
				{
					// Symbol price halves per character after the first; shift is validated by the
					// chain against maxTokenSymbolLength, mirror that bound here.
					var shift = SymbolShift(p.SymbolLength, config.maxTokenSymbolLength, nameof(p.SymbolLength));
					if (v2)
					{
						workUnits = 0;
						policyFee = SaturatingAdd(config.policyFeeCreateTokenBase,
							p.SymbolLength > 0 ? config.policyFeeCreateTokenSymbol >> shift : 0);
					}
					else
					{
						workUnits = SaturatingAdd(config.gasFeeCreateTokenBase,
							p.SymbolLength > 0 ? config.gasFeeCreateTokenSymbol >> shift : 0);
					}
					break;
				}
			case NativeFeeKind.CreateTokenSeries:
				workUnits = v2 ? 0 : config.gasFeeCreateTokenSeries;
				policyFee = v2 ? config.policyFeeCreateTokenSeries : 0;
				break;
			case NativeFeeKind.RegisterName:
				{
					if (p.NameLength <= 0)
						throw new ArgumentOutOfRangeException(nameof(p), "NameLength is required for RegisterName");
					var shift = SymbolShift(p.NameLength, config.maxNameLength, nameof(p.NameLength));
					workUnits = v2 ? 0 : config.gasFeeRegisterName >> shift;
					policyFee = v2 ? config.policyFeeRegisterName >> shift : 0;
					break;
				}
			case NativeFeeKind.Script:
				workUnits = p.ScriptUnitsAllowance;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(kind));
		}

		var rows = p.FreshRows >= 0 ? (ulong)p.FreshRows : DefaultFreshRows(kind, count, p.RomRamBytes);
		var envelope = p.EnvelopeBytes != 0 ? p.EnvelopeBytes : DefaultEnvelopeBytes(kind, count, p.RomRamBytes);
		// Native TxTypes store no events in the block; script txs do (Notify), so budget some.
		ulong eventBytes = kind == NativeFeeKind.Script ? 512UL : 0UL;

		ulong expected, maxGas;
		if (v2)
		{
			// v2 formula: bill = MulShiftSat(workUnits + blockData*25, mult, shift) + policyFee,
			// floored at minimumGasBill. blockData = envelope + events + net storage quanta
			// (quanta are added to the byte count by the chain formula).
			expected = BillV2(workUnits, envelope, eventBytes, rows, policyFee, config);
			// Offer headroom: +25% over the padded bill covers witness-size wiggle and event
			// variance; deterministic and always refunded down to the actual bill.
			var padded = BillV2(workUnits, RoundUp(envelope, 128), eventBytes, rows, policyFee, config);
			maxGas = SaturatingAdd(padded, padded / 4);
			if (maxGas < config.minimumGasBill)
				maxGas = config.minimumGasBill;
			if (maxGas < config.minimumGasOffer)
				maxGas = config.minimumGasOffer;
		}
		else
		{
			// v1 formula: bill = (workUnits * mult >> shift) + blockData * gasFeePerByte where
			// blockData = payload + events + net storage quanta (no envelope term, no floor).
			var work = MulShift(workUnits, config.feeMultiplier, config.feeShift);
			var blockData = SaturatingAdd(SaturatingAdd(p.PayloadBytes, eventBytes), rows);
			expected = SaturatingAdd(work, SaturatingMul(blockData, config.gasFeePerByte));
			// Offer shape mirrors the validator's own test-agent stdFee: a 2x minimum-offer pad
			// plus a flat 1KiB block-data allowance on top of the work term.
			var byteAllowance = blockData > 1024 ? blockData : 1024;
			maxGas = SaturatingAdd(SaturatingAdd(config.minimumGasOffer * 2, work),
				SaturatingMul(byteAllowance, config.gasFeePerByte));
		}

		var maxData = SaturatingMul(rows, config.dataEscrowPerRow);
		return new NativeFeeEstimate(maxGas, maxData, expected);
	}

	/// <summary>
	/// Envelope size (signed tx bytes as carried in the block) from a serialized unsigned
	/// message length and the number of signers. Use with
	/// <see cref="NativeFeeParams.EnvelopeBytes"/> for exact v2 estimates. Witness layout
	/// mirrors SignedTxMsg: native TxTypes append bare 64-byte signatures (one, or two for the
	/// _GasPayer variants); Call/Trade/Phantasma txs append a length-prefixed witness array
	/// (32-byte address + 64-byte signature per entry).
	/// </summary>
	public static uint EnvelopeBytesFor(NativeFeeKind kind, int serializedMessageLength, int witnessCount = 1)
	{
		if (serializedMessageLength < 0)
			throw new ArgumentOutOfRangeException(nameof(serializedMessageLength));
		if (witnessCount < 0)
			throw new ArgumentOutOfRangeException(nameof(witnessCount));
		switch (kind)
		{
			case NativeFeeKind.TransferFungible:
			case NativeFeeKind.TransferNonFungible:
			case NativeFeeKind.MintFungible:
			case NativeFeeKind.MintNonFungible:
			case NativeFeeKind.BurnFungible:
			case NativeFeeKind.BurnNonFungible:
				return (uint)serializedMessageLength + NativeSignatureBytes * (uint)witnessCount;
			default:
				// CreateToken/CreateTokenSeries/RegisterName ride TxTypes.Call; Script rides
				// TxTypes.Phantasma - both carry the witness array form.
				return (uint)serializedMessageLength + 4 + WitnessArrayEntryBytes * (uint)witnessCount;
		}
	}

	private static ulong BillV2(ulong workUnits, ulong envelope, ulong eventBytes, ulong rows, ulong policyFee, in GasConfig config)
	{
		var blockData = SaturatingAdd(SaturatingAdd(envelope, eventBytes), rows);
		var byteUnits = SaturatingMul(blockData, GasModelV2UnitsPerBlockDataByte);
		var bill = MulShift(SaturatingAdd(workUnits, byteUnits), config.feeMultiplier, config.feeShift);
		bill = SaturatingAdd(bill, policyFee);
		return bill < config.minimumGasBill ? config.minimumGasBill : bill;
	}

	// Chain fee scaling: (value * feeMultiplier) >> feeShift with a wide intermediate, saturating
	// to ulong. Matches the validator's v2 MulShiftSaturateU64; for sane v1 configs (live values
	// never overflow 64 bits) it is also bit-identical to the v1 math.
	private static ulong MulShift(ulong value, ulong multiplier, byte shift)
	{
		if (shift >= 64)
			return 0; // the chain clamps oversized shifts to a zero delta
		var wide = (BigInteger)value * multiplier >> shift;
		return wide > ulong.MaxValue ? ulong.MaxValue : (ulong)wide;
	}

	private static int SymbolShift(int length, byte maxLength, string paramName)
	{
		if (length < 0)
			throw new ArgumentOutOfRangeException(paramName);
		if (length == 0)
			return 0;
		var shift = length - 1;
		// The chain asserts shift < maxNameLength / maxTokenSymbolLength; a longer input could
		// never be admitted, so reject it here instead of quoting a fee for an impossible tx.
		if (maxLength != 0 && shift >= maxLength)
			throw new ArgumentOutOfRangeException(paramName, $"length {length} exceeds the chain maximum {maxLength}");
		return shift;
	}

	// Worst-case paid rows the operation can create (drives MaxData). Refund-only operations
	// (burns) need no escrow allowance: refunds never require maxData budget.
	private static ulong DefaultFreshRows(NativeFeeKind kind, uint count, uint romRamBytes)
	{
		switch (kind)
		{
			case NativeFeeKind.TransferFungible:
			case NativeFeeKind.MintFungible:
				return 1; // recipient balance row may be fresh (SOUL/KCAL rows would be free)
			case NativeFeeKind.TransferNonFungible:
				// Per instance the chain deletes the sender's NFT-lookup row and creates the
				// recipient's (creation escrows at the current price, the deletion refunds the
				// old row's own deposit) + possibly a fresh recipient balance row.
				return (ulong)count + 1;
			case NativeFeeKind.MintNonFungible:
				// Per instance: owner row + lookup row + the instance state rows holding ROM/RAM
				// (1 KiB quanta), plus possibly a fresh recipient balance row.
				return (ulong)count * (2 + CeilDiv(romRamBytes, 1024)) + 1;
			case NativeFeeKind.BurnFungible:
			case NativeFeeKind.BurnNonFungible:
				return 0;
			case NativeFeeKind.CreateToken:
				return 8; // token info + symbol lookup + supply/config rows, metadata-dependent
			case NativeFeeKind.CreateTokenSeries:
				return 4;
			case NativeFeeKind.RegisterName:
				return 2; // name->address and address->name rows
			case NativeFeeKind.Script:
			default:
				return 4;
		}
	}

	// Conservative single-witness envelope defaults per kind; used only when the caller did not
	// measure the real signed size. Generous by design: under v2 an oversized estimate only
	// raises the refunded offer, never the settled bill.
	private static ulong DefaultEnvelopeBytes(NativeFeeKind kind, uint count, uint romRamBytes)
	{
		switch (kind)
		{
			case NativeFeeKind.TransferFungible:
			case NativeFeeKind.MintFungible:
			case NativeFeeKind.BurnFungible:
			case NativeFeeKind.RegisterName:
				return 512;
			case NativeFeeKind.TransferNonFungible:
			case NativeFeeKind.BurnNonFungible:
				return 512 + 8UL * count; // 8 bytes per carried instance id
			case NativeFeeKind.MintNonFungible:
				return 512 + (ulong)count * (64 + (ulong)romRamBytes); // ROM/RAM ride the envelope
			case NativeFeeKind.CreateToken:
				return 4096; // token metadata (icons, descriptions) dominates
			case NativeFeeKind.CreateTokenSeries:
				return 2048;
			case NativeFeeKind.Script:
			default:
				return 1024;
		}
	}

	private static ulong CeilDiv(ulong value, ulong divisor) => value / divisor + (value % divisor != 0 ? 1UL : 0UL);

	private static ulong RoundUp(ulong value, ulong step) => CeilDiv(value, step) * step;

	private static ulong SaturatingAdd(ulong a, ulong b) => a > ulong.MaxValue - b ? ulong.MaxValue : a + b;

	private static ulong SaturatingMul(ulong a, ulong b)
	{
		if (a == 0 || b == 0)
			return 0;
		return a > ulong.MaxValue / b ? ulong.MaxValue : a * b;
	}
}
