using System.Numerics;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>Native operations the Tier-1 fee calculator models exactly.</summary>
public enum NativeFeeKind
{
	/// <summary>Fungible token transfer (TxTypes.TransferFungible and its _GasPayer variant).</summary>
	TransferFungible,
	/// <summary>NFT transfer of <c>Count</c> instances (TxTypes.TransferNonFungible_* and _GasPayer variants).</summary>
	TransferNonFungible,
	/// <summary>Fungible mint (TxTypes.MintFungible).</summary>
	MintFungible,
	/// <summary>
	/// NFT mint of <c>Count</c> instances with caller-supplied ROM (TxTypes.MintNonFungible). The ROM
	/// is stored exactly as submitted, unlike a deterministic Phantasma mint.
	/// </summary>
	MintNonFungible,
	/// <summary>
	/// Deterministic Phantasma NFT mint of <c>Count</c> instances (TokenContract.MintPhantasmaNonFungible).
	/// The chain stores a canonical ROM: the public ROM plus the derived Phantasma NFT id plus a copy
	/// of the public ROM, so storage grows at twice the ROM size.
	/// </summary>
	MintPhantasmaNonFungible,
	/// <summary>Fungible burn (TxTypes.BurnFungible and its _GasPayer variant).</summary>
	BurnFungible,
	/// <summary>NFT burn of <c>Count</c> instances (TxTypes.BurnNonFungible and its _GasPayer variant).</summary>
	BurnNonFungible,
	/// <summary>TokenContract.CreateToken call. Requires <c>SymbolLength</c> when a symbol is set.</summary>
	CreateToken,
	/// <summary>TokenContract.CreateTokenSeries call.</summary>
	CreateTokenSeries,
	/// <summary>GovernanceContract.RegisterName call. Requires <c>NameLength</c>.</summary>
	RegisterName,
	/// <summary>
	/// Generic Phantasma VM script transaction (AllowGas/SpendGas pattern: stake, marketplace, custom
	/// contract calls). Script opcode costs depend on chain state and are not closed-form; the
	/// estimate budgets <c>ScriptUnitsAllowance</c> VM work units and <c>ScriptEventBytes</c> of events
	/// on top of the byte fee. For an exact script bill use the node-side estimator (estimateTransaction).
	/// </summary>
	Script
}

/// <summary>
/// Inputs of <see cref="NativeFeeEstimator.EstimateNativeFee"/>. Under gas model v2 every byte the
/// transaction puts in the block is billed and every new storage row is escrowed, so the inputs are
/// the sizes the chain will see: the signed envelope, the serialized structures the operation stores,
/// and the facts about existing state that decide whether a row is new.
/// <para>
/// The inputs are of two kinds, and they are defaulted differently:
/// </para>
/// <list type="bullet">
/// <item>Facts the CALLER CANNOT KNOW without reading chain state - whether the recipient already
/// holds the token, whether a ROM carries an <c>_i</c> id, which mode a series mints in. Each defaults
/// to the case that costs MORE, so an estimate built from defaults is an upper bound the settlement
/// can only undercut, never a short offer.</item>
/// <item>Facts carried by the MESSAGE ITSELF - the instance count, the serialized sizes, whether the
/// token being created is non-fungible or carries <c>pre_burn</c>. These have no safe default because
/// they are not guesses: pass them. <c>FeePlanner.Plan</c> reads every one of them out of the message.</item>
/// </list>
/// </summary>
public sealed class NativeFeeParams
{
	/// <summary>
	/// Full signed transaction size in bytes - the envelope carried in the block. Required under gas
	/// model v2 (see <see cref="NativeFeeEstimator.EnvelopeBytesFor"/> and
	/// <see cref="SignedTxMsg.EnvelopeBytes"/>); ignored under v1, which billed only the payload note.
	/// </summary>
	public uint? EnvelopeBytes { get; set; }

	/// <summary>Instance count for NFT kinds (transferred / minted / burned instances). Default 1.</summary>
	public uint Count { get; set; } = 1;

	/// <summary>
	/// Token moved by a transfer / mint / burn. Balance rows of the chain's gas and data tokens are
	/// free, so with the token id known the estimate escrows nothing for them.
	/// </summary>
	public ulong? TokenId { get; set; }

	/// <summary>The recipient already holds this token, so its balance row exists and costs nothing. Default false.</summary>
	public bool RecipientHoldsToken { get; set; }

	/// <summary>
	/// The recipient is an NFT-derived address (an infusion): the chain reads that NFT's owner - one
	/// extra query fee. Transfers and every mint kind pay it; a burn has no recipient. This is a fact
	/// of the recipient's address form, not of chain state - <c>FeePlanner.Plan</c> derives it from the
	/// message's own recipient (<c>TokenHelper.IsNftAddress</c>) - so only direct callers of this
	/// calculator pass it.
	/// </summary>
	public bool ToIsNftAddress { get; set; }

	/// <summary>
	/// The token's balances can exceed int64 (a big-fungible token). A fungible mint or burn answers
	/// with the RESULTING balance as a variable-length integer - 9 bytes while it fits int64, up to
	/// 33 for an int256 balance - and the resulting balance is chain state, so the default (null)
	/// prices the 33-byte maximum: a covering bound, refunded down. Pass <c>false</c> for an ordinary
	/// int64 token and the estimate is exact.
	/// </summary>
	public bool? BigFungible { get; set; }

	/// <summary>The token has been burned before, so its burnt counter row exists. Default false (first burn creates it).</summary>
	public bool TokenBurnedBefore { get; set; }

	/// <summary>
	/// The token's supply-tracking row exists. The chain drops that row when its balance reaches
	/// exactly zero, so this is chain state with two absent-row edges: a limited-supply token whose
	/// entire supply is in circulation (the next burn recreates the row) and an unlimited token with
	/// nothing outstanding (the next mint recreates it). Unstated, every mint and burn prices the
	/// recreation - one more storage quantum in the bill and the escrow ceiling - so the default
	/// covers both edges; pass <c>true</c> for the exact quote whenever the token is not at one of
	/// them. Rows of the chain's gas and data tokens are free either way.
	/// </summary>
	public bool SupplyRowExists { get; set; }

	/// <summary>Token symbol length in characters (CreateToken). 0 = no symbol.</summary>
	public int SymbolLength { get; set; }

	/// <summary>Serialized <c>TokenInfo</c> length (CreateToken) - the Call arguments; it becomes the token-info row.</summary>
	public uint TokenInfoBytes { get; set; }

	/// <summary>The token being created is non-fungible (CreateToken): one more row, the series counter.</summary>
	public bool NonFungible { get; set; }

	/// <summary>The token metadata carries <c>pre_burn</c> (CreateToken): the burnt counter row is created at once.</summary>
	public bool HasPreBurn { get; set; }

	/// <summary>The token metadata carries an inflation schedule (CreateToken): the next-inflation row is created.</summary>
	public bool HasInflationSchedule { get; set; }

	/// <summary>Serialized <c>SeriesInfo</c> length (CreateTokenSeries) - the Call arguments after the token id.</summary>
	public uint SeriesInfoBytes { get; set; }

	/// <summary>
	/// The series metadata carries a <c>_i</c> id (CreateTokenSeries): the meta-id lookup row is
	/// created. Schema-encoded like the ROM, so the default (null) is true - the row that may be billed.
	/// </summary>
	public bool? SeriesHasMetaId { get; set; }

	/// <summary>Registered name length in characters (RegisterName). Required for that kind.</summary>
	public int NameLength { get; set; }

	/// <summary>
	/// ROM bytes of every minted or burned instance (MintNonFungible / BurnNonFungible: as stored;
	/// MintPhantasmaNonFungible: the public ROM). <see cref="RomBytesPerInstance"/> overrides it
	/// when the instances differ.
	/// </summary>
	public uint RomBytes { get; set; }

	/// <summary>ROM bytes per instance, one entry per instance, when they differ. Overrides <see cref="RomBytes"/>.</summary>
	public uint[]? RomBytesPerInstance { get; set; }

	/// <summary>RAM bytes of every instance. Default 0 (no RAM row). <see cref="RamBytesPerInstance"/> overrides it.</summary>
	public uint RamBytes { get; set; }

	/// <summary>RAM bytes per instance, one entry per instance, when they differ. Overrides <see cref="RamBytes"/>.</summary>
	public uint[]? RamBytesPerInstance { get; set; }

	/// <summary>
	/// The raw ROM carries a <c>_i</c> id, which the chain indexes in one more row (MintNonFungible /
	/// BurnNonFungible). The ROM is schema-encoded, so a caller holding only the bytes cannot tell, and
	/// the default (null) is true - on a mint that is the reading which escrows for the row, and on a
	/// burn it is the reading that mirrors what the mint created. The burn does not PRICE on it either
	/// way (see <see cref="NativeFeeEstimate.DeletedStorageQuanta"/>); a Phantasma mint always has one
	/// and ignores this input.
	/// </summary>
	public bool? RomHasMetaId { get; set; }

	/// <summary>
	/// The series mints duplicated NFTs (MintPhantasmaNonFungible). A duplicated series costs one more
	/// query fee per instance than a unique one, plus one per distinct series (see
	/// <see cref="DistinctSeriesCount"/>). A call whose instances mix duplicated and unique series is
	/// priced as if every instance were duplicated.
	/// <para>
	/// Default (null) is true. A series' mode is chain state the message does not carry, so the
	/// costlier reading is the only safe one: a duplicated mint priced as unique is short by exactly
	/// those query fees, and the planner offers the bill with no headroom, so it aborts. Pass
	/// <c>false</c> only when the series is known to be unique - the saving is a few query fees.
	/// </para>
	/// </summary>
	public bool? DuplicatedSeries { get; set; }

	/// <summary>
	/// How many distinct series a duplicated Phantasma mint writes into (MintPhantasmaNonFungible with
	/// <see cref="DuplicatedSeries"/>). The chain reads each series' supply once per transaction, not
	/// once per instance, so this is the count of distinct series ids in the call - never more than
	/// <see cref="Count"/>. Default 1. Ignored for a unique series, which does not read the supply at all.
	/// </summary>
	public uint DistinctSeriesCount { get; set; } = 1;

	/// <summary>User payload bytes attached to the tx (billed under gas model v1 only).</summary>
	public uint PayloadBytes { get; set; }

	/// <summary>
	/// VM work-unit allowance for the Script kind. The default (5000) exceeds every script seen in
	/// mainnet history (max 3392 units) with margin.
	/// </summary>
	public ulong ScriptUnitsAllowance { get; set; } = 5000;

	/// <summary>Event bytes allowance for the Script kind (Notify payloads count as block data). Default 512.</summary>
	public uint ScriptEventBytes { get; set; } = 512;

	/// <summary>New storage quanta allowance for the Script kind. Default 4.</summary>
	public uint ScriptStorageQuanta { get; set; } = 4;
}

/// <summary>
/// A fee quote for one transaction, from the Tier-1 calculator or from the node's own estimator.
/// Gas values are kcal-base (1 KCAL = 1e10 kcal-base); escrow is in data-token atoms
/// (1 SOUL = 1e8 atoms).
/// </summary>
public readonly struct FeeQuote
{
	/// <summary>
	/// The gas offer that covers the bill (<c>TxMsg.maxGas</c>). From the Tier-1 calculator it is the
	/// bill itself, floored at the chain's minimum offer; unused gas is refunded, so callers wanting
	/// headroom add it on top.
	/// </summary>
	public ulong MaxGas { get; }

	/// <summary>The storage-escrow ceiling (<c>TxMsg.maxData</c>): every new row priced at the current row price.</summary>
	public ulong MaxData { get; }

	/// <summary>
	/// The bill the chain formula yields for exactly the provided inputs - exact for every native
	/// operation when the inputs describe the transaction and the state facts are right. For the
	/// Script kind it is the budgeted allowance, not a prediction.
	/// <para>
	/// One caveat on "exact": the chain scales each charge as it is made and adds the results, while
	/// this calculator scales their sum. The two agree while <c>feeShift</c> is zero, which is the case
	/// on every network running the v2 model today; under a non-zero shift the rounding differs, always
	/// in the direction of this calculator quoting a few units MORE than the chain settles, so the
	/// offer stays covering.
	/// </para>
	/// </summary>
	public ulong ExpectedGasBill { get; }

	public FeeQuote(ulong maxGas, ulong maxData, ulong expectedGasBill)
	{
		MaxGas = maxGas;
		MaxData = maxData;
		ExpectedGasBill = expectedGasBill;
	}
}

/// <summary>Result of a Tier-1 fee estimate: the quote plus the storage rows it was computed from.</summary>
public readonly struct NativeFeeEstimate
{
	/// <inheritdoc cref="FeeQuote.MaxGas"/>
	public ulong MaxGas { get; }
	/// <inheritdoc cref="FeeQuote.MaxData"/>
	public ulong MaxData { get; }
	/// <inheritdoc cref="FeeQuote.ExpectedGasBill"/>
	public ulong ExpectedGasBill { get; }

	/// <summary>Storage quanta the operation creates (1024-byte units per new paid row).</summary>
	public uint NewStorageQuanta { get; }

	/// <summary>
	/// Storage quanta the operation deletes; their escrow is refunded at each row's own price.
	/// <para>
	/// Informational. It does not enter the bill: <see cref="MaxData"/> covers the rows an operation
	/// CREATES, and the block-data term uses the net growth, which an operation that deletes more than
	/// it creates floors at zero either way. A burn's figure is therefore a lower bound - the stored
	/// ROM is chain state the message does not carry - and nothing depends on tightening it.
	/// </para>
	/// </summary>
	public uint DeletedStorageQuanta { get; }

	public NativeFeeEstimate(ulong maxGas, ulong maxData, ulong expectedGasBill, uint newStorageQuanta, uint deletedStorageQuanta)
	{
		MaxGas = maxGas;
		MaxData = maxData;
		ExpectedGasBill = expectedGasBill;
		NewStorageQuanta = newStorageQuanta;
		DeletedStorageQuanta = deletedStorageQuanta;
	}

	/// <summary>The quote alone, the shape the node-side estimator also produces.</summary>
	public FeeQuote ToQuote() => new(MaxGas, MaxData, ExpectedGasBill);
}

/// <summary>
/// Tier-1 fee calculator: the exact gas bill and storage escrow of a native operation under both gas
/// models (selected by <c>GasConfig.version</c>), reproducing the chain's own settlement arithmetic
/// and the gas each contract path charges. Any change to those formulas ships as a new gas-model
/// version, never silently, which is what makes an offline calculation safe.
/// </summary>
public static class NativeFeeEstimator
{
	/// <summary>
	/// Gas model v2 price of block-carried bytes, in gas units per byte. A versioned consensus constant
	/// of the v2 gas model, deliberately not part of the on-chain config: it changes only with a new gas
	/// model version, so a client can hold it as a constant rather than read it per block.
	/// </summary>
	public const ulong GasModelV2UnitsPerBlockDataByte = 25;

	/// <summary>Storage is escrowed per 1024-byte quantum of a row's key plus its value.</summary>
	public const uint StorageQuantumBytes = 1024;

	/// <summary>Serialized size of one witness-array entry (32-byte address + 64-byte signature).</summary>
	public const uint WitnessArrayEntryBytes = 96;

	/// <summary>Serialized size of one bare signature (native TxTypes carry no witness array).</summary>
	public const uint NativeSignatureBytes = 64;

	// Sizes of the token-module rows a native operation writes, in bytes of key plus value. A row costs
	// ceil((key + value) / 1024) quanta; the small fixed rows never leave the first quantum, while the
	// ROM-bearing rows are computed from the ROM the caller submits.
	private const uint NftInstanceRowOverhead = 17 + 32 + 8 + 1 + 4; // key + originator + created + flags + ROM length prefix
	private const uint NftRamRowOverhead = 17; // key; the RAM is stored bare
	private const uint TokenInfoKeyBytes = 9;
	private const uint SeriesInfoKeyBytes = 13;
	// The canonical ROM of a deterministic Phantasma mint: the public ROM fields, plus `_i` (int256,
	// 32 bytes), plus a `rom` field holding the public ROM again with a 4-byte length prefix.
	private const uint PhantasmaCanonicalRomOverhead = 32 + 4;
	// Fungible mint / burn calls return the resulting balance as an IntX: 1 header + 8 bytes for
	// int64 balances, up to 1 + 32 for int256 (big-fungible) balances.
	private const uint IntXSmallResultBytes = 9;
	private const uint IntXBigResultBytes = 33;

	/// <summary>
	/// Longest name or symbol this calculator will price. The chain's length-halved policy fee is
	/// defined up to here; past it no offline price exists, so the calculator refuses rather than
	/// quote a number the chain may not agree with.
	/// </summary>
	private const int MaxPriceableLength = 64;

	private readonly struct OperationModel
	{
		public readonly ulong WorkUnits;
		public readonly ulong PolicyFee;
		/// <summary>Bytes the Call returns; they are block data like the envelope.</summary>
		public readonly uint ResultBytes;
		public readonly uint NewQuanta;
		public readonly uint DeletedQuanta;

		public OperationModel(ulong workUnits, ulong policyFee, uint resultBytes, uint newQuanta, uint deletedQuanta)
		{
			WorkUnits = workUnits;
			PolicyFee = policyFee;
			ResultBytes = resultBytes;
			NewQuanta = newQuanta;
			DeletedQuanta = deletedQuanta;
		}
	}

	/// <summary>
	/// The exact gas bill and storage escrow of a native operation.
	/// </summary>
	/// <param name="kind">Operation kind.</param>
	/// <param name="config">Current chain gas config (see the getGasConfig RPC method).</param>
	/// <param name="p">The transaction's sizes and the state facts that decide row creation.</param>
	public static NativeFeeEstimate EstimateNativeFee(NativeFeeKind kind, in GasConfig config, NativeFeeParams? p = null)
	{
		p ??= new NativeFeeParams();
		if (p.Count < 1)
			throw new ArgumentOutOfRangeException(nameof(p), "Count must be a positive integer");
		var v2 = UsesGasModelV2(config);
		var model = OperationModelFor(kind, config, p);
		// Only the net growth of paid storage is block data; deleted rows are refunded, not billed.
		var netQuanta = model.NewQuanta > model.DeletedQuanta ? model.NewQuanta - model.DeletedQuanta : 0u;

		ulong expected, maxGas;
		if (v2)
		{
			if (!p.EnvelopeBytes.HasValue)
				throw new ArgumentException("EnvelopeBytes is required under gas model v2", nameof(p));
			// v2: bill = mulShiftSat(work + blockData * 25, mult, shift) + policyFee, floored at
			// minimumGasBill, where blockData = envelope + net storage quanta + Call result bytes.
			var blockData = (ulong)p.EnvelopeBytes.Value + netQuanta + model.ResultBytes;
			var byteUnits = SaturatingMul(blockData, GasModelV2UnitsPerBlockDataByte);
			var bill = MulShift(SaturatingAdd(model.WorkUnits, byteUnits), config.feeMultiplier, config.feeShift);
			bill = SaturatingAdd(bill, model.PolicyFee);
			expected = bill < config.minimumGasBill ? config.minimumGasBill : bill;
			maxGas = expected < config.minimumGasOffer ? config.minimumGasOffer : expected;
		}
		else
		{
			// v1: bill = (work * mult >> shift) + blockData * gasFeePerByte, where blockData = payload +
			// Call result bytes + net storage quanta; no envelope term, no floor. The v1 product prices
			// ride the work term (see OperationModelFor).
			var work = MulShift(model.WorkUnits, config.feeMultiplier, config.feeShift);
			var blockData = (ulong)p.PayloadBytes + model.ResultBytes + netQuanta;
			expected = SaturatingAdd(work, SaturatingMul(blockData, config.gasFeePerByte));
			// Offer shape mirrors the validator's own test-agent stdFee: a 2x minimum-offer pad plus a
			// flat 1 KiB block-data allowance on top of the work term.
			var byteAllowance = blockData > 1024 ? blockData : 1024;
			maxGas = SaturatingAdd(SaturatingAdd(SaturatingMul(config.minimumGasOffer, 2), work), SaturatingMul(byteAllowance, config.gasFeePerByte));
		}

		return new NativeFeeEstimate(
			maxGas,
			SaturatingMul(model.NewQuanta, config.dataEscrowPerRow),
			expected,
			model.NewQuanta,
			model.DeletedQuanta);
	}

	/// <summary>
	/// Envelope size (signed tx bytes as carried in the block) from an already serialized unsigned
	/// message length and the number of signers, for callers that hold bytes rather than a message.
	/// With the message in hand prefer <see cref="SignedTxMsg.EnvelopeBytes"/>, which reads the
	/// witness count out of the message instead of taking it on trust. Witness layout: the native
	/// transaction types append bare 64-byte signatures, while the call, trade and script types append
	/// a length-prefixed array of 32-byte address plus 64-byte signature entries.
	/// <para>
	/// <paramref name="witnessCount"/> has no default on purpose. A fee kind does not distinguish a
	/// <c>_GasPayer</c> message, which carries two signatures, from the plain form that carries one -
	/// both are the same kind - so a default of one would silently size a two-signature envelope as a
	/// one-signature envelope and under-offer the transaction by 64 bytes.
	/// </para>
	/// </summary>
	public static uint EnvelopeBytesFor(NativeFeeKind kind, int serializedMessageLength, int witnessCount)
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
				// CreateToken / CreateTokenSeries / MintPhantasmaNonFungible / RegisterName ride
				// TxTypes.Call; Script rides TxTypes.Phantasma - both carry the witness array form.
				return (uint)serializedMessageLength + 4 + WitnessArrayEntryBytes * (uint)witnessCount;
		}
	}

	/// <summary>Storage quanta of one row: ceil((key + value) / 1024).</summary>
	public static uint StorageQuantaFor(uint rowBytes) => (rowBytes + StorageQuantumBytes - 1) / StorageQuantumBytes;

	/// <summary>
	/// Bytes of the NFT ROM the chain stores for a deterministic Phantasma mint, from the public ROM the
	/// caller submits: the chain builds a canonical ROM out of the public fields, the derived Phantasma
	/// NFT id and a second copy of the public ROM, so storage grows at roughly twice the submitted size.
	/// </summary>
	public static uint PhantasmaCanonicalRomBytes(uint publicRomBytes) => publicRomBytes * 2 + PhantasmaCanonicalRomOverhead;

	// Work units, policy fee, result bytes and row changes of each operation. Query fees (gasFeeQuery)
	// are charged by the state lookups a contract path makes internally, so they are part of the bill
	// even though nothing in the message mentions them.
	private static OperationModel OperationModelFor(NativeFeeKind kind, in GasConfig config, NativeFeeParams p)
	{
		var v2 = UsesGasModelV2(config);
		var count = p.Count;
		var freeBalanceRows = p.TokenId.HasValue && (p.TokenId.Value == config.gasTokenId || p.TokenId.Value == config.dataTokenId);
		var recipientRow = p.RecipientHoldsToken || freeBalanceRows ? 0u : 1u;
		var burntRow = p.TokenBurnedBefore || freeBalanceRows ? 0u : 1u;
		// The supply-tracking row a mint or burn may have to recreate (see NativeFeeParams). Transfers
		// never touch it, and a creation writes it unconditionally, so only mints and burns price it.
		var supplyRow = p.SupplyRowExists || freeBalanceRows ? 0u : 1u;
		var balanceResultBytes = (p.BigFungible ?? true) ? IntXBigResultBytes : IntXSmallResultBytes;
		var infusionQuery = p.ToIsNftAddress ? config.gasFeeQuery : 0;

		switch (kind)
		{
			case NativeFeeKind.TransferFungible:
				return new OperationModel(SaturatingAdd(config.gasFeeTransfer, infusionQuery), 0, 0, recipientRow, 0);
			case NativeFeeKind.TransferNonFungible:
				// Per instance the owner's lookup row is deleted and the recipient's created; the
				// recipient's balance row may be new as well.
				return new OperationModel(
					SaturatingAdd(SaturatingMul(config.gasFeeTransfer, count), infusionQuery), 0, 0, count + recipientRow, count);
			case NativeFeeKind.MintFungible:
				return new OperationModel(
					SaturatingAdd(config.gasFeeTransfer, infusionQuery), 0, balanceResultBytes, recipientRow + supplyRow, 0);
			case NativeFeeKind.BurnFungible:
				return new OperationModel(config.gasFeeTransfer, 0, balanceResultBytes, burntRow + supplyRow, 0);
			case NativeFeeKind.MintNonFungible:
				{
					var roms = PerInstance(p.RomBytes, p.RomBytesPerInstance, count, nameof(p.RomBytesPerInstance));
					var rams = PerInstance(p.RamBytes, p.RamBytesPerInstance, count, nameof(p.RamBytesPerInstance));
					// Per instance: the instance row (ROM), the owner row, the lookup row, the RAM row when
					// RAM is given, the meta-id row when the ROM carries `_i`; plus the recipient's balance row
					// and the supply row when it must be recreated.
					var romHasMetaId = p.RomHasMetaId ?? true;
					var quanta = recipientRow + supplyRow;
					for (var i = 0; i < count; i++)
					{
						quanta += StorageQuantaFor(NftInstanceRowOverhead + roms[i]) + 2;
						if (rams[i] > 0) quanta += StorageQuantaFor(NftRamRowOverhead + rams[i]);
						if (romHasMetaId) quanta += 1;
					}
					return new OperationModel(
						SaturatingAdd(SaturatingMul(config.gasFeeTransfer, count), infusionQuery),
						0,
						4 + 8 * count, // instance count + one u64 instance id each
						quanta,
						0);
				}
			case NativeFeeKind.MintPhantasmaNonFungible:
				{
					var roms = PerInstance(p.RomBytes, p.RomBytesPerInstance, count, nameof(p.RomBytesPerInstance));
					var rams = PerInstance(p.RamBytes, p.RamBytesPerInstance, count, nameof(p.RamBytesPerInstance));
					// As MintNonFungible, with the canonical ROM stored and the meta-id row always present.
					var quanta = recipientRow + supplyRow;
					for (var i = 0; i < count; i++)
					{
						quanta += StorageQuantaFor(NftInstanceRowOverhead + PhantasmaCanonicalRomBytes(roms[i])) + 3;
						if (rams[i] > 0) quanta += StorageQuantaFor(NftRamRowOverhead + rams[i]);
					}
					// Per instance: the mint itself, the series lookup by meta id, and the token-info read the
					// series-mode check performs. A duplicated series reads the token info a SECOND time per
					// instance, to pick up the series' shared ROM, and reads that series' supply once per
					// distinct series in the call - the chain remembers the supply it already read, so the
					// supply fee does not scale with the instance count the way the other three do.
					var duplicatedSeries = p.DuplicatedSeries ?? true;
					var queriesPerInstance = duplicatedSeries ? 3UL : 2UL;
					var seriesSupplyQueries = duplicatedSeries ? DistinctSeries(p) : 0UL;
					var perInstance = SaturatingAdd(config.gasFeeTransfer, SaturatingMul(config.gasFeeQuery, queriesPerInstance));
					var work = SaturatingAdd(
						SaturatingAdd(SaturatingMul(perInstance, count), SaturatingMul(config.gasFeeQuery, seriesSupplyQueries)),
						infusionQuery);
					return new OperationModel(
						work,
						0,
						4 + 40 * count, // instance count + (32-byte Phantasma id + u64 instance id) each
						quanta,
						0);
				}
			case NativeFeeKind.BurnNonFungible:
				{
					var roms = PerInstance(p.RomBytes, p.RomBytesPerInstance, count, nameof(p.RomBytesPerInstance));
					var rams = PerInstance(p.RamBytes, p.RamBytesPerInstance, count, nameof(p.RamBytesPerInstance));
					// The instance, owner, lookup (and RAM, meta-id) rows are deleted and refunded; the burnt
					// counter row is created on the token's first burn, and the supply row when it must be
					// recreated. Each instance's infusion sweep reads the NFT address balances twice. The
					// deleted rows mirror what the mint created, which is why the meta-id row is counted the
					// same way here - but see DeletedStorageQuanta: on a burn this total is reported, never
					// billed.
					var romHasMetaId = p.RomHasMetaId ?? true;
					var deleted = 0u;
					for (var i = 0; i < count; i++)
					{
						deleted += StorageQuantaFor(NftInstanceRowOverhead + roms[i]) + 2;
						if (rams[i] > 0) deleted += StorageQuantaFor(NftRamRowOverhead + rams[i]);
						if (romHasMetaId) deleted += 1;
					}
					return new OperationModel(
						SaturatingMul(SaturatingAdd(config.gasFeeTransfer, SaturatingMul(config.gasFeeQuery, 2)), count),
						0,
						0,
						burntRow + supplyRow,
						deleted);
				}
			case NativeFeeKind.CreateToken:
				{
					var shift = SymbolShift(p.SymbolLength, config.maxTokenSymbolLength, nameof(p.SymbolLength));
					var hasSymbol = p.SymbolLength > 0;
					// Rows: the symbol lookup, the token info, the null-address supply row, the series counter
					// for NFT tokens, the burnt counter with pre_burn, the next-inflation row with a schedule.
					var quanta =
						(hasSymbol ? 1u : 0u) +
						StorageQuantaFor(TokenInfoKeyBytes + p.TokenInfoBytes) +
						1u +
						(p.NonFungible ? 1u : 0u) +
						(p.HasPreBurn ? 1u : 0u) +
						(p.HasInflationSchedule ? 1u : 0u);
					var basePrice = v2 ? config.policyFeeCreateTokenBase : config.gasFeeCreateTokenBase;
					var symbolPrice = hasSymbol ? (v2 ? config.policyFeeCreateTokenSymbol : config.gasFeeCreateTokenSymbol) >> shift : 0;
					var price = SaturatingAdd(basePrice, symbolPrice);
					return new OperationModel(
						v2 ? 0 : price,
						v2 ? price : 0,
						8, // the new token id, u64
						quanta,
						0);
				}
			case NativeFeeKind.CreateTokenSeries:
				{
					// Rows: the series info, the series supply, the meta-id lookup when the metadata has `_i`.
					var seriesHasMetaId = p.SeriesHasMetaId ?? true;
					var quanta = StorageQuantaFor(SeriesInfoKeyBytes + p.SeriesInfoBytes) + 1u + (seriesHasMetaId ? 1u : 0u);
					return new OperationModel(
						v2 ? 0 : config.gasFeeCreateTokenSeries,
						v2 ? config.policyFeeCreateTokenSeries : 0,
						4, // the new series id, u32
						quanta,
						0);
				}
			case NativeFeeKind.RegisterName:
				{
					if (p.NameLength <= 0)
						throw new ArgumentException("NameLength is required for RegisterName", nameof(p));
					var shift = SymbolShift(p.NameLength, config.maxNameLength, nameof(p.NameLength));
					// Governance-module rows are free data: the two name rows escrow nothing.
					return new OperationModel(
						v2 ? 0 : config.gasFeeRegisterName >> shift,
						v2 ? config.policyFeeRegisterName >> shift : 0,
						0,
						0,
						0);
				}
			case NativeFeeKind.Script:
				return new OperationModel(p.ScriptUnitsAllowance, 0, p.ScriptEventBytes, p.ScriptStorageQuanta, 0);
			default:
				throw new ArgumentOutOfRangeException(nameof(kind), $"unknown fee kind: {kind}");
		}
	}

	// Distinct series a duplicated mint touches, defaulting to one. More series than instances is
	// impossible - every series in the call is written into by at least one instance - and catching it
	// here turns a caller's bookkeeping slip into an error instead of an over-offer nobody notices.
	private static ulong DistinctSeries(NativeFeeParams p)
	{
		if (p.DistinctSeriesCount < 1 || p.DistinctSeriesCount > p.Count)
			throw new ArgumentOutOfRangeException(nameof(p), $"DistinctSeriesCount must be between 1 and {p.Count}");
		return p.DistinctSeriesCount;
	}

	private static uint[] PerInstance(uint each, uint[]? perInstance, uint count, string name)
	{
		if (perInstance == null)
		{
			var same = new uint[count];
			for (var i = 0; i < count; i++) same[i] = each;
			return same;
		}
		if (perInstance.Length != count)
			throw new ArgumentException($"{name} must have one entry per instance ({count})", nameof(perInstance));
		return perInstance;
	}

	// Chain fee scaling: (value * feeMultiplier) >> feeShift, saturating to ulong. Saturation is what the
	// chain does under gas model v2, so a hostile config cannot wrap a bill; under v1 the live values
	// never approach 64 bits, so the same expression is bit-identical to the v1 arithmetic.
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
			throw new ArgumentOutOfRangeException(paramName, "length must be a non-negative integer");
		if (length == 0)
			return 0;
		var shift = length - 1;
		// The chain asserts shift < maxNameLength / maxTokenSymbolLength; a longer input could never
		// be admitted, so reject it here instead of quoting a fee for an impossible tx.
		if (maxLength != 0 && shift >= maxLength)
			throw new ArgumentOutOfRangeException(paramName, $"length {length} exceeds the chain maximum {maxLength}");
		// Refusing beats guessing: see MaxPriceableLength. The transaction may well be admitted - this
		// says only that no honest price can be quoted for it offline.
		if (length > MaxPriceableLength)
			throw new ArgumentOutOfRangeException(paramName, $"length {length} is longer than {MaxPriceableLength} and cannot be priced offline");
		return shift;
	}

	// Which gas model a config selects: the plain version field, the one fact every copy of a config
	// carries whatever container it travelled in.
	private static bool UsesGasModelV2(in GasConfig config) => config.version >= 1;

	private static ulong SaturatingAdd(ulong a, ulong b) => a > ulong.MaxValue - b ? ulong.MaxValue : a + b;

	private static ulong SaturatingMul(ulong a, ulong b)
	{
		if (a == 0 || b == 0)
			return 0;
		return a > ulong.MaxValue / b ? ulong.MaxValue : a * b;
	}
}
