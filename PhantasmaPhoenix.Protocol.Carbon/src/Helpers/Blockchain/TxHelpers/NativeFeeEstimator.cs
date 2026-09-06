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
	/// is stored exactly as submitted. A deterministic Phantasma mint stores a canonical ROM.
	/// <para>
	/// The chain refuses this operation where governance has not allowed caller-supplied ROM ids, and
	/// that is the state of the public chains today. The SDK carries no builder for it. The model is
	/// kept because the calculator prices the operation correctly wherever it is allowed.
	/// </para>
	/// </summary>
	MintNonFungible,
	/// <summary>
	/// Deterministic Phantasma NFT mint of <c>Count</c> instances (TokenContract.MintPhantasmaNonFungible).
	/// The chain stores a canonical ROM. It holds the public ROM, the derived Phantasma NFT id and a
	/// second copy of the public ROM, so storage grows at twice the ROM size.
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
	/// contract calls). Script opcode costs depend on chain state, and no formula gives them. The
	/// estimate budgets <c>ScriptUnitsAllowance</c> VM work units and <c>ScriptEventBytes</c> of events
	/// on top of the byte fee. For an exact script bill use the node-side estimator
	/// (estimateTransaction).
	/// </summary>
	Script
}

/// <summary>
/// Inputs of <see cref="NativeFeeEstimator.EstimateNativeFee"/>. Under gas model v2 the chain bills
/// every byte the transaction puts in the block and escrows every new storage row. The inputs are
/// therefore the sizes the chain will see: the signed envelope, the serialized structures the
/// operation stores, and the facts about existing state that decide whether a row is new.
/// <para>
/// The inputs are of two kinds, and they are defaulted differently.
/// </para>
/// <list type="bullet">
/// <item>Facts the CALLER CANNOT KNOW without reading chain state. Examples: whether the recipient
/// already holds the token, whether a ROM carries an <c>_i</c> id, which mode a series mints in. Each
/// defaults to the case that costs MORE. An estimate built from defaults is then an upper bound, and
/// the settlement can only come out below it.</item>
/// <item>Facts carried by the MESSAGE ITSELF. Examples: the instance count, the serialized sizes,
/// whether the token being created is non-fungible or carries <c>pre_burn</c>. These are not guesses,
/// so they have no safe default. Pass them. <c>FeePlanner.Plan</c> reads every one of them out of the
/// message.</item>
/// </list>
/// </summary>
public sealed class NativeFeeParams
{
	/// <summary>
	/// Full signed transaction size in bytes. This is the envelope the block carries. Required under
	/// gas model v2 (see <see cref="NativeFeeEstimator.EnvelopeBytesFor"/> and
	/// <see cref="SignedTxMsg.EnvelopeBytes"/>). Ignored under v1, which billed the payload note
	/// alone.
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
	/// The recipient is an NFT-derived address, which means an infusion. The chain reads that NFT's
	/// owner, and that costs one extra query fee. Transfers and every mint kind pay it. A burn has no
	/// recipient.
	/// <para>
	/// This is a fact of the recipient's address form. It says nothing about chain state.
	/// <c>FeePlanner.Plan</c> derives it from the message's own recipient with
	/// <c>TokenHelper.IsNftAddress</c>, so only direct callers of this calculator pass it.
	/// </para>
	/// </summary>
	public bool ToIsNftAddress { get; set; }

	/// <summary>
	/// The token's balances can exceed int64. Such a token is called big-fungible.
	/// <para>
	/// A fungible mint or burn answers with the RESULTING balance as a variable-length integer. That
	/// is 9 bytes while the balance fits int64, and up to 33 bytes for an int256 balance. The
	/// resulting balance is chain state, so the default (null) prices the 33-byte maximum. The offer
	/// then covers the bill and the difference is refunded. Pass <c>false</c> for an ordinary int64
	/// token and the estimate is exact.
	/// </para>
	/// </summary>
	public bool? BigFungible { get; set; }

	/// <summary>The token has been burned before, so its burnt counter row exists. Default false (first burn creates it).</summary>
	public bool TokenBurnedBefore { get; set; }

	/// <summary>
	/// The token's supply-tracking row exists. The chain drops that row when its balance reaches
	/// exactly zero, so the row can be absent in two cases. A limited-supply token has its entire
	/// supply in circulation, and the next burn recreates the row. An unlimited token has nothing
	/// outstanding, and the next mint recreates it.
	/// <para>
	/// When this fact is unstated, every mint and burn prices the recreation. That is one more storage
	/// quantum in the bill and in the escrow ceiling, and it covers both cases. Pass <c>true</c> for
	/// the exact quote whenever the token is in neither of them. Rows of the chain's gas and data
	/// tokens are free either way.
	/// </para>
	/// </summary>
	public bool SupplyRowExists { get; set; }

	/// <summary>
	/// What the burned NFTs hold at their own addresses (BurnNonFungible). There is one entry per
	/// asset per burned instance.
	/// <para>
	/// The burn returns every one of them to the burner, and the chain charges for each. A fungible
	/// token costs a transfer fee plus the owner-lookup query of the NFT-address source. An NFT token
	/// costs an instance query, a transfer per instance, and that same lookup. A returned token the
	/// burner does not hold also costs the burner's new balance row.
	/// </para>
	/// <para>
	/// This is chain state that the message does not carry, and an NFT can hold any number of assets,
	/// so there is no costlier bound to assume. Null prices an empty address, because a direct caller
	/// of this calculator states what it knows. <c>FeePlanner.Plan</c> demands the list, and
	/// <c>PhantasmaAPI.Fees</c> reads it from the chain.
	/// </para>
	/// </summary>
	public IReadOnlyList<InfusedAsset>? Infusions { get; set; }

	/// <summary>Token symbol length in characters (CreateToken). 0 = no symbol.</summary>
	public int SymbolLength { get; set; }

	/// <summary>Serialized <c>TokenInfo</c> length (CreateToken). These are the Call arguments, and
	/// they become the token-info row.</summary>
	public uint TokenInfoBytes { get; set; }

	/// <summary>The token being created is non-fungible (CreateToken). It costs one more row, the
	/// series counter.</summary>
	public bool NonFungible { get; set; }

	/// <summary>The token metadata carries <c>pre_burn</c> (CreateToken). The burnt counter row is then
	/// created at once.</summary>
	public bool HasPreBurn { get; set; }

	/// <summary>The token metadata carries an inflation schedule (CreateToken). The next-inflation row
	/// is then created.</summary>
	public bool HasInflationSchedule { get; set; }

	/// <summary>The token metadata names a staking organisation (CreateToken). The creation looks that
	/// organisation up, which costs one query fee.</summary>
	public bool HasStakingOrganisation { get; set; }

	/// <summary>The token metadata names a staking reward token (CreateToken). The creation reads that
	/// token's info, which costs one query fee.</summary>
	public bool HasStakingRewardToken { get; set; }

	/// <summary>Serialized <c>SeriesInfo</c> length (CreateTokenSeries). These are the Call arguments
	/// after the token id.</summary>
	public uint SeriesInfoBytes { get; set; }

	/// <summary>
	/// The series metadata carries a <c>_i</c> id (CreateTokenSeries). The meta-id lookup row is then
	/// created. The metadata is schema-encoded like the ROM, so a caller holding only the bytes cannot
	/// tell. The default (null) is true, which is the reading that pays for the row.
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
	/// BurnNonFungible). The ROM is schema-encoded, so a caller holding only the bytes cannot tell.
	/// The default (null) is true. On a mint that is the reading which escrows for the row. On a burn
	/// it is the reading that mirrors what the mint created. A burn never prices on this fact either
	/// way (see <see cref="NativeFeeEstimate.DeletedStorageQuanta"/>). A Phantasma mint always has
	/// such an id and ignores this input.
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
	/// <c>false</c> only when the series is known to be unique. The saving is a few query fees.
	/// </para>
	/// </summary>
	public bool? DuplicatedSeries { get; set; }

	/// <summary>
	/// How many distinct series a duplicated Phantasma mint writes into (MintPhantasmaNonFungible with
	/// <see cref="DuplicatedSeries"/>). The chain reads each series' supply once per transaction, not
	/// once per instance, so this is the count of distinct series ids in the call. It is never more
	/// than <see cref="Count"/>. Default 1. Ignored for a unique series, which does not read the
	/// supply at all.
	/// </summary>
	public uint DistinctSeriesCount { get; set; } = 1;

	/// <summary>User payload bytes attached to the tx (billed under gas model v1 only).</summary>
	public uint PayloadBytes { get; set; }

	/// <summary>
	/// VM work-unit allowance for the Script kind. The default (5000) exceeds every script seen in
	/// mainnet history (max 3392 units) with margin.
	/// <para>
	/// In a <c>Call_Multi</c> the allowance counts once per unmodelled call, because each of them can
	/// do that much work. A batch of calls the model does not price therefore offers several times
	/// what it will spend. The localnet measured 4.2x for one such call and 10.1x for ten. The
	/// difference is refunded, and a caller who knows the calls can bring the offer down with this
	/// field.
	/// </para>
	/// </summary>
	public ulong ScriptUnitsAllowance { get; set; } = 5000;

	/// <summary>
	/// Event bytes allowance for the Script kind (Notify payloads count as block data). Default 512,
	/// per unmodelled call like <see cref="ScriptUnitsAllowance"/>.
	/// </summary>
	public uint ScriptEventBytes { get; set; } = 512;

	/// <summary>
	/// New storage quanta allowance for the Script kind. Default 4, per unmodelled call like
	/// <see cref="ScriptUnitsAllowance"/>.
	/// </summary>
	public uint ScriptStorageQuanta { get; set; } = 4;
}

/// <summary>
/// One operation of a batched message. It carries the kind the operation is priced as and the inputs
/// it is priced from. See <see cref="NativeFeeEstimator.EstimateNativeFeeBatch"/>.
/// </summary>
public sealed class NativeFeePart
{
	public NativeFeeKind Kind { get; }
	public NativeFeeParams Params { get; }

	public NativeFeePart(NativeFeeKind kind, NativeFeeParams? p = null)
	{
		Kind = kind;
		Params = p ?? new NativeFeeParams();
	}
}

/// <summary>
/// The estimate inputs that belong to the transaction itself. No operation inside it owns them,
/// because the block carries one envelope however many operations the message performs.
/// </summary>
public sealed class NativeFeeTransaction
{
	/// <inheritdoc cref="NativeFeeParams.EnvelopeBytes"/>
	public uint? EnvelopeBytes { get; set; }
	/// <inheritdoc cref="NativeFeeParams.PayloadBytes"/>
	public uint PayloadBytes { get; set; }
}

/// <summary>
/// An asset held at a burned NFT's own address. The burn returns it to the burner. See
/// <see cref="NativeFeeParams.Infusions"/>.
/// </summary>
public sealed class InfusedAsset
{
	/// <summary>
	/// The token's id. Rows of the chain's gas and data tokens are free, and only the id tells which
	/// token this is. Null prices the rows as paid, so the escrow ceiling can only come out too high.
	/// </summary>
	public ulong? TokenId { get; set; }

	/// <summary>True for an NFT token. The burn returns every instance the address holds, see
	/// <see cref="InstanceCount"/>.</summary>
	public bool NonFungible { get; set; }

	/// <summary>How many instances of an NFT token the address holds. Each one costs a transfer and
	/// moves a lookup row. Default 1.</summary>
	public uint InstanceCount { get; set; } = 1;

	/// <summary>
	/// The burner already holds this token, so no balance row is created when it comes back. Default
	/// false, which is the costlier reading. It moves the escrow ceiling and never the bill, because a
	/// burn refunds more rows than the return creates.
	/// </summary>
	public bool BurnerHoldsToken { get; set; }
}

/// <summary>
/// A fee quote for one transaction. It comes from the Tier-1 calculator or from the node's own
/// estimator. Gas values are in kcal-base, where 1 KCAL is 1e10 kcal-base. Escrow is in data-token
/// atoms, where 1 SOUL is 1e8 atoms.
/// </summary>
public readonly struct FeeQuote
{
	/// <summary>
	/// The gas offer that covers the bill (<c>TxMsg.maxGas</c>). The Tier-1 calculator returns the
	/// bill itself, floored at the chain's minimum offer. Unused gas is refunded, so a caller who
	/// wants headroom adds it on top.
	/// </summary>
	public ulong MaxGas { get; }

	/// <summary>The storage-escrow ceiling (<c>TxMsg.maxData</c>). It prices every new row at the
	/// current row price.</summary>
	public ulong MaxData { get; }

	/// <summary>
	/// The bill the chain formula yields for exactly the provided inputs. It is exact for every
	/// native operation when the inputs describe the transaction and the state facts are right. For
	/// the Script kind it is the budgeted allowance, and the chain settles below it.
	/// <para>
	/// One caveat on the word exact. The chain scales each charge as it is made and adds the results.
	/// This calculator scales their sum. The two agree when <c>feeShift</c> is zero, and that is the
	/// case on every network running the v2 model today. Under a non-zero shift the rounding differs.
	/// The difference always goes one way: this calculator quotes a few units MORE than the chain
	/// settles, so the offer still covers the bill.
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

/// <summary>Result of a Tier-1 fee estimate. It holds the quote and the storage rows it was computed
/// from.</summary>
public readonly struct NativeFeeEstimate
{
	/// <inheritdoc cref="FeeQuote.MaxGas"/>
	public ulong MaxGas { get; }
	/// <inheritdoc cref="FeeQuote.MaxData"/>
	public ulong MaxData { get; }
	/// <inheritdoc cref="FeeQuote.ExpectedGasBill"/>
	public ulong ExpectedGasBill { get; }

	/// <summary>Storage quanta the operation creates. One quantum is 1024 bytes of a new paid
	/// row.</summary>
	public uint NewStorageQuanta { get; }

	/// <summary>
	/// Storage quanta the operation deletes. Their escrow is refunded at each row's own price.
	/// <para>
	/// The property is informational and does not enter the bill. <see cref="MaxData"/> covers the rows
	/// an operation CREATES. The block-data term uses the net growth, and an operation that deletes
	/// more than it creates floors that growth at zero anyway.
	/// </para>
	/// <para>
	/// A burn's figure is a lower bound, because the stored ROM is chain state that the message does
	/// not carry. Nothing depends on tightening it.
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
/// The Tier-1 fee calculator. It returns the exact gas bill and storage escrow of a native operation
/// under both gas models, selected by <c>GasConfig.version</c>. It reproduces the chain's own
/// settlement arithmetic together with the gas each contract path charges.
/// <para>
/// An offline calculation is safe here because any change to those formulas ships as a new gas-model
/// version. It never changes silently.
/// </para>
/// </summary>
public static class NativeFeeEstimator
{
	/// <summary>
	/// Gas model v2 price of block-carried bytes, in gas units per byte.
	/// <para>
	/// This is a versioned consensus constant of the v2 gas model, and it is kept out of the on-chain
	/// config on purpose. It changes only with a new gas model version, so a client can hold it as a
	/// constant and never read it per block.
	/// </para>
	/// </summary>
	public const ulong GasModelV2UnitsPerBlockDataByte = 25;

	/// <summary>Storage is escrowed per 1024-byte quantum of a row's key plus its value.</summary>
	public const uint StorageQuantumBytes = 1024;

	/// <summary>Serialized size of one witness-array entry, which is a 32-byte address plus a 64-byte
	/// signature.</summary>
	public const uint WitnessArrayEntryBytes = 96;

	/// <summary>Serialized size of one bare signature. The native TxTypes carry no witness
	/// array.</summary>
	public const uint NativeSignatureBytes = 64;

	// Sizes of the token-module rows a native operation writes, in bytes of key plus value. A row costs
	// ceil((key + value) / 1024) quanta. The small fixed rows never leave the first quantum. The
	// ROM-bearing rows are computed from the ROM the caller submits.
	private const uint NftInstanceRowOverhead = 17 + 32 + 8 + 1 + 4; // key + originator + created + flags + ROM length prefix
	private const uint NftRamRowOverhead = 17; // key; the RAM is stored bare
	private const uint TokenInfoKeyBytes = 9;
	private const uint SeriesInfoKeyBytes = 13;
	// The canonical ROM of a deterministic Phantasma mint holds three things: the public ROM fields,
	// the `_i` id (int256, 32 bytes), and a `rom` field with the public ROM again behind a 4-byte
	// length prefix.
	private const uint PhantasmaCanonicalRomOverhead = 32 + 4;
	// Fungible mint and burn calls return the resulting balance as an IntX. That is 1 header byte plus
	// 8 bytes for an int64 balance, and up to 1 plus 32 for an int256 balance on a big-fungible
	// token.
	private const uint IntXSmallResultBytes = 9;
	private const uint IntXBigResultBytes = 33;

	/// <summary>
	/// Longest name or symbol this calculator will price. The chain's length-halved policy fee is
	/// defined up to this length. Past it no offline price exists, so the calculator refuses. A quoted
	/// number could differ from what the chain charges.
	/// </summary>
	private const int MaxPriceableLength = 64;

	private readonly struct OperationModel
	{
		public readonly ulong WorkUnits;
		public readonly ulong PolicyFee;
		/// <summary>Bytes the Call returns. They are block data, like the envelope.</summary>
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
		return EstimateNativeFeeBatch(
			new[] { new NativeFeePart(kind, p) },
			config,
			new NativeFeeTransaction { EnvelopeBytes = p.EnvelopeBytes, PayloadBytes = p.PayloadBytes });
	}

	/// <summary>
	/// Returns the fee of a message that performs SEVERAL operations in one transaction. That message
	/// is a <c>TxTypes.Call_Multi</c>. The chain runs its calls in a plain loop, with no per-call
	/// surcharge and no batch dispatch cost. It accumulates one gas bill, one result buffer and one
	/// change set over the whole transaction, and it bills the envelope once. A batch therefore costs
	/// the sum of its parts over work, policy fee, result bytes and rows, settled once.
	/// <para>
	/// Rows are counted per part. Two parts that create the SAME row count it twice. Two burns of one
	/// token both count its burnt counter, and two transfers into one fresh address both count its
	/// balance row.
	/// </para>
	/// <para>
	/// Carrying "an earlier part already created it" forward is unsound in the direction that matters.
	/// A burn that empties a supply to exactly zero deletes the supply row again, so a later mint
	/// would be priced short, abort and be billed. Counting twice raises the escrow ceiling alone, and
	/// that is refunded.
	/// </para>
	/// </summary>
	/// <param name="parts">The operations the message performs, in call order.</param>
	/// <param name="config">Current chain gas config (see the getGasConfig RPC method).</param>
	/// <param name="transaction">The sizes that belong to the transaction itself, not to one operation
	/// inside it.</param>
	public static NativeFeeEstimate EstimateNativeFeeBatch(IReadOnlyList<NativeFeePart> parts, in GasConfig config, NativeFeeTransaction? transaction = null)
	{
		transaction ??= new NativeFeeTransaction();
		// Work, policy fees, result bytes and rows add up. The envelope is counted once. The fee
		// scaling and the minimum-bill floor apply to the total. The chain does the same with the
		// counters it accumulates while the transaction runs.
		ulong workUnits = 0, policyFee = 0;
		uint resultBytes = 0, newQuanta = 0, deletedQuanta = 0;
		foreach (var part in parts)
		{
			var p = part.Params;
			if (p.Count < 1)
				throw new ArgumentOutOfRangeException(nameof(parts), "Count must be a positive integer");
			var model = OperationModelFor(part.Kind, config, p);
			workUnits = SaturatingAdd(workUnits, model.WorkUnits);
			policyFee = SaturatingAdd(policyFee, model.PolicyFee);
			// Every counter saturates rather than wrapping. A message cannot carry enough calls to
			// overflow the 32-bit ones, but a silent wrap here would quote a fee far below the bill.
			resultBytes = SaturatingAdd(resultBytes, model.ResultBytes);
			newQuanta = SaturatingAdd(newQuanta, model.NewQuanta);
			deletedQuanta = SaturatingAdd(deletedQuanta, model.DeletedQuanta);
		}
		// Only the net growth of paid storage is block data. Deleted rows are refunded.
		var netQuanta = newQuanta > deletedQuanta ? newQuanta - deletedQuanta : 0u;

		ulong expected, maxGas;
		if (UsesGasModelV2(config))
		{
			if (!transaction.EnvelopeBytes.HasValue)
				throw new ArgumentException("EnvelopeBytes is required under gas model v2", nameof(transaction));
			// v2: bill = mulShiftSat(work + blockData * 25, mult, shift) + policyFee, floored at
			// minimumGasBill, where blockData = envelope + net storage quanta + Call result bytes.
			var blockData = (ulong)transaction.EnvelopeBytes.Value + netQuanta + resultBytes;
			var byteUnits = SaturatingMul(blockData, GasModelV2UnitsPerBlockDataByte);
			var bill = MulShift(SaturatingAdd(workUnits, byteUnits), config.feeMultiplier, config.feeShift);
			bill = SaturatingAdd(bill, policyFee);
			expected = bill < config.minimumGasBill ? config.minimumGasBill : bill;
			maxGas = expected < config.minimumGasOffer ? config.minimumGasOffer : expected;
		}
		else
		{
			// v1: bill = (work * mult >> shift) + blockData * gasFeePerByte, where blockData = payload +
			// Call result bytes + net storage quanta. There is no envelope term and no floor. The v1
			// product prices ride the work term, see OperationModelFor.
			var work = MulShift(workUnits, config.feeMultiplier, config.feeShift);
			var blockData = (ulong)transaction.PayloadBytes + resultBytes + netQuanta;
			expected = SaturatingAdd(work, SaturatingMul(blockData, config.gasFeePerByte));
			// The offer has the same shape as the validator's own test-agent stdFee. It is a 2x
			// minimum-offer pad plus a flat 1 KiB block-data allowance on top of the work term.
			var byteAllowance = blockData > 1024 ? blockData : 1024;
			maxGas = SaturatingAdd(SaturatingAdd(SaturatingMul(config.minimumGasOffer, 2), work), SaturatingMul(byteAllowance, config.gasFeePerByte));
		}

		return new NativeFeeEstimate(
			maxGas,
			SaturatingMul(newQuanta, config.dataEscrowPerRow),
			expected,
			newQuanta,
			deletedQuanta);
	}

	/// <summary>
	/// Returns the envelope size, which is the signed transaction as the block carries it. It is
	/// computed from the length of an already serialized unsigned message and the number of signers.
	/// Use it when you hold bytes and no message. With the message in hand use
	/// <see cref="SignedTxMsg.EnvelopeBytes"/>, which reads the witness count out of the message and
	/// takes nothing on trust.
	/// <para>
	/// The witness layout differs by type. The native transaction types append bare 64-byte signatures.
	/// The call, trade and script types append a length-prefixed array of entries, each a 32-byte
	/// address plus a 64-byte signature.
	/// </para>
	/// <para>
	/// <paramref name="witnessCount"/> has no default on purpose. A fee kind does not tell a
	/// <c>_GasPayer</c> message, which carries two signatures, from the plain form that carries one.
	/// Both are the same kind. A default of one would size a two-signature envelope as a one-signature
	/// envelope and under-offer the transaction by 64 bytes.
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
				// CreateToken, CreateTokenSeries, MintPhantasmaNonFungible and RegisterName ride
				// TxTypes.Call. Script rides TxTypes.Phantasma. Both carry the witness array form.
				return (uint)serializedMessageLength + 4 + WitnessArrayEntryBytes * (uint)witnessCount;
		}
	}

	/// <summary>Returns the storage quanta of one row, which is ceil((key + value) / 1024).</summary>
	public static uint StorageQuantaFor(uint rowBytes) => (rowBytes + StorageQuantumBytes - 1) / StorageQuantumBytes;

	/// <summary>
	/// Returns the bytes of the NFT ROM the chain stores for a deterministic Phantasma mint, computed
	/// from the public ROM the caller submits. The chain builds a canonical ROM out of the public
	/// fields, the derived Phantasma NFT id and a second copy of the public ROM. Storage therefore
	/// grows at roughly twice the submitted size.
	/// </summary>
	public static uint PhantasmaCanonicalRomBytes(uint publicRomBytes) => publicRomBytes * 2 + PhantasmaCanonicalRomOverhead;

	// Work units, policy fee, result bytes and row changes of each operation. A contract path makes
	// state lookups internally, and each one is charged as a query fee (gasFeeQuery). Those fees are
	// part of the bill even though nothing in the message mentions them.
	private static OperationModel OperationModelFor(NativeFeeKind kind, in GasConfig config, NativeFeeParams p)
	{
		var v2 = UsesGasModelV2(config);
		var count = p.Count;
		var freeBalanceRows = p.TokenId.HasValue && (p.TokenId.Value == config.gasTokenId || p.TokenId.Value == config.dataTokenId);
		var recipientRow = p.RecipientHoldsToken || freeBalanceRows ? 0u : 1u;
		var burntRow = p.TokenBurnedBefore || freeBalanceRows ? 0u : 1u;
		// The supply-tracking row a mint or burn may have to recreate, see NativeFeeParams. Transfers
		// never touch it, and a creation always writes it, so only mints and burns price it.
		var supplyRow = p.SupplyRowExists || freeBalanceRows ? 0u : 1u;
		var balanceResultBytes = (p.BigFungible ?? true) ? IntXBigResultBytes : IntXSmallResultBytes;
		var infusionQuery = p.ToIsNftAddress ? config.gasFeeQuery : 0;

		switch (kind)
		{
			case NativeFeeKind.TransferFungible:
				return new OperationModel(SaturatingAdd(config.gasFeeTransfer, infusionQuery), 0, 0, recipientRow, 0);
			case NativeFeeKind.TransferNonFungible:
				// Per instance, the owner's lookup row is deleted and the recipient's one is created. The
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
					// Per instance the mint writes the instance row with the ROM, the owner row and the lookup
					// row. It writes the RAM row when RAM is given, and the meta-id row when the ROM carries
					// `_i`. On top of that come the recipient's balance row and the supply row when it must be
					// recreated.
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
					// The same rows as MintNonFungible. The stored ROM is the canonical one and the meta-id row
					// is always present.
					var quanta = recipientRow + supplyRow;
					for (var i = 0; i < count; i++)
					{
						quanta += StorageQuantaFor(NftInstanceRowOverhead + PhantasmaCanonicalRomBytes(roms[i])) + 3;
						if (rams[i] > 0) quanta += StorageQuantaFor(NftRamRowOverhead + rams[i]);
					}
					// Per instance: the mint itself, the series lookup by meta id, and the token-info read the
					// series-mode check performs. A duplicated series reads the token info a SECOND time per
					// instance, to pick up the series' shared ROM. It also reads that series' supply once per
					// distinct series in the call. The chain remembers a supply it has already read, so the
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
					// The instance, owner and lookup rows are deleted and refunded, and so are the RAM and
					// meta-id rows when they exist. The burnt counter row is created on the token's first
					// burn, and the supply row when it must be recreated.
					//
					// Each instance's infusion sweep reads the NFT address balances twice. Whatever the
					// sweep finds is returned to the burner and charged as the transfers it takes, see
					// ReturnedAssets.
					//
					// The deleted rows mirror what the mint created, and that is why the meta-id row is
					// counted the same way here. On a burn this total is reported and never billed, see
					// DeletedStorageQuanta.
					var romHasMetaId = p.RomHasMetaId ?? true;
					var deleted = 0u;
					for (var i = 0; i < count; i++)
					{
						deleted += StorageQuantaFor(NftInstanceRowOverhead + roms[i]) + 2;
						if (rams[i] > 0) deleted += StorageQuantaFor(NftRamRowOverhead + rams[i]);
						if (romHasMetaId) deleted += 1;
					}
					var returned = ReturnedAssets(p, config);
					return new OperationModel(
						SaturatingAdd(SaturatingMul(SaturatingAdd(config.gasFeeTransfer, SaturatingMul(config.gasFeeQuery, 2)), count), returned.WorkUnits),
						0,
						0,
						burntRow + supplyRow + returned.NewQuanta,
						deleted + returned.DeletedQuanta);
				}
			case NativeFeeKind.CreateToken:
				{
					var shift = SymbolShift(p.SymbolLength, config.maxTokenSymbolLength, nameof(p.SymbolLength));
					var hasSymbol = p.SymbolLength > 0;
					// The creation writes the symbol lookup row, the token info row and the null-address
					// supply row. An NFT token adds the series counter. pre_burn adds the burnt counter. An
					// inflation schedule adds the next-inflation row.
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
					// Validating the metadata looks up a staking organisation it names and reads a reward
					// token it names. Each costs one query fee, on top of the policy fee.
					var metadataQueries = SaturatingMul(config.gasFeeQuery, (p.HasStakingOrganisation ? 1UL : 0UL) + (p.HasStakingRewardToken ? 1UL : 0UL));
					return new OperationModel(
						SaturatingAdd(v2 ? 0 : price, metadataQueries),
						v2 ? price : 0,
						8, // the new token id, u64
						quanta,
						0);
				}
			case NativeFeeKind.CreateTokenSeries:
				{
					// The call writes the series info row and the series supply row. The metadata adds the
					// meta-id lookup row when it has `_i`.
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
					// Governance-module rows are free data, so the two name rows escrow nothing.
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

	// What burning the NFTs gives back to the burner, priced as the transfers the chain performs. A
	// fungible token costs one transfer plus the owner lookup of the NFT-address source. An NFT token
	// costs one instance query, one transfer per instance, and that same lookup.
	//
	// Rows: a returned token the burner does not hold creates a balance row. That row is paid unless
	// the token is the gas or data token, and only the id tells which token it is, so an unknown id is
	// priced as paid. Every returned instance moves its lookup row, and the NFT address's own rows are
	// deleted.
	//
	// The deletions always match or exceed the creations, so the returns never add block data. They
	// add work, and they add rows to the escrow ceiling.
	private static OperationModel ReturnedAssets(NativeFeeParams p, in GasConfig config)
	{
		ulong work = 0;
		uint created = 0, deleted = 0;
		if (p.Infusions == null)
			return new OperationModel(0, 0, 0, 0, 0);
		foreach (var asset in p.Infusions)
		{
			if (asset == null)
				throw new ArgumentException("Infusions must not contain null entries", nameof(p));
			var freeRows = asset.TokenId.HasValue && (asset.TokenId.Value == config.gasTokenId || asset.TokenId.Value == config.dataTokenId);
			var balanceRow = asset.BurnerHoldsToken || freeRows ? 0u : 1u;
			if (asset.NonFungible)
			{
				if (asset.InstanceCount < 1)
					throw new ArgumentOutOfRangeException(nameof(p), "InstanceCount of a returned NFT token must be a positive integer");
				work = SaturatingAdd(work, SaturatingAdd(SaturatingMul(config.gasFeeQuery, 2), SaturatingMul(config.gasFeeTransfer, asset.InstanceCount)));
				created += balanceRow + asset.InstanceCount;
				deleted += 1 + asset.InstanceCount;
			}
			else
			{
				work = SaturatingAdd(work, SaturatingAdd(config.gasFeeTransfer, config.gasFeeQuery));
				created += balanceRow;
				deleted += freeRows ? 0u : 1u;
			}
		}
		return new OperationModel(work, 0, 0, created, deleted);
	}

	// Returns how many distinct series a duplicated mint touches. The default is one. More series than
	// instances is impossible, because at least one instance writes into every series in the call. The
	// check turns a caller's bookkeeping slip into an error. Without it the slip becomes an over-offer
	// that nobody notices.
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

	// Chain fee scaling: (value * feeMultiplier) >> feeShift, saturating to ulong. The chain saturates
	// the same way under gas model v2, so a hostile config cannot wrap a bill. Under v1 the live values
	// never approach 64 bits, so this expression is bit-identical to the v1 arithmetic.
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
		// The chain asserts that the shift stays below maxNameLength and maxTokenSymbolLength. A longer
		// input could never be admitted, so it is rejected here. Quoting a fee for an impossible
		// transaction would be worse.
		if (maxLength != 0 && shift >= maxLength)
			throw new ArgumentOutOfRangeException(paramName, $"length {length} exceeds the chain maximum {maxLength}");
		// The calculator refuses instead of guessing, see MaxPriceableLength. The transaction may well
		// be admitted. This says only that no honest price can be quoted for it offline.
		if (length > MaxPriceableLength)
			throw new ArgumentOutOfRangeException(paramName, $"length {length} is longer than {MaxPriceableLength} and cannot be priced offline");
		return shift;
	}

	// Returns which gas model a config selects. It reads the plain version field, which is the one
	// fact every copy of a config carries whatever container it travelled in.
	private static bool UsesGasModelV2(in GasConfig config) => config.version >= 1;

	private static ulong SaturatingAdd(ulong a, ulong b) => a > ulong.MaxValue - b ? ulong.MaxValue : a + b;

	private static uint SaturatingAdd(uint a, uint b) => a > uint.MaxValue - b ? uint.MaxValue : a + b;

	private static ulong SaturatingMul(ulong a, ulong b)
	{
		if (a == 0 || b == 0)
			return 0;
		return a > ulong.MaxValue / b ? ulong.MaxValue : a * b;
	}
}
