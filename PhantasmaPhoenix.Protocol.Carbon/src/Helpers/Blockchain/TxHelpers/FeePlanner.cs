using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Facts about chain state and signing that a message does not carry. The fee depends on them.
/// <para>
/// A state fact left null takes the calculator's default, which is the reading that costs more. A
/// plan that states nothing is therefore an upper bound, and the settlement can only come out below
/// it. Each default is documented on <see cref="NativeFeeParams"/>.
/// </para>
/// <para>
/// <see cref="Infusions"/> has no costlier reading, because a burned NFT can hold any number of
/// assets. The planner demands the list, and <c>PhantasmaAPI.Fees</c> reads it from the chain.
/// </para>
/// <para>
/// The facts the message itself carries are absent here: counts, sizes, token ids, the NFT-address
/// recipient, <c>nonFungible</c>, <c>pre_burn</c>. <see cref="FeePlanner.Plan"/> reads those out of
/// the message, and a value from the caller could only contradict it.
/// </para>
/// <para>
/// Every field is optional and most callers set none. They are listed roughly in order of how likely
/// a caller is to know the answer. An ordinary wallet may well know the first four. The next three
/// need the token's schema or the series' metadata. The last three size the allowance for a VM
/// script, and no formula predicts what a script costs.
/// </para>
/// </summary>
public class FeePlanOptions
{
	/// <inheritdoc cref="NativeFeeParams.RecipientHoldsToken"/>
	public bool? RecipientHoldsToken { get; set; }
	/// <inheritdoc cref="NativeFeeParams.BigFungible"/>
	public bool? BigFungible { get; set; }
	/// <inheritdoc cref="NativeFeeParams.TokenBurnedBefore"/>
	public bool? TokenBurnedBefore { get; set; }
	/// <inheritdoc cref="NativeFeeParams.SupplyRowExists"/>
	public bool? SupplyRowExists { get; set; }
	/// <inheritdoc cref="NativeFeeParams.DuplicatedSeries"/>
	public bool? DuplicatedSeries { get; set; }
	/// <inheritdoc cref="NativeFeeParams.RomHasMetaId"/>
	public bool? RomHasMetaId { get; set; }
	/// <inheritdoc cref="NativeFeeParams.SeriesHasMetaId"/>
	public bool? SeriesHasMetaId { get; set; }
	/// <summary>
	/// What the burned NFT holds at its own address (BurnNonFungible). The burn returns it to the
	/// burner and the chain charges for it, see <see cref="NativeFeeParams.Infusions"/>.
	/// <para>
	/// Required to plan a burn through <see cref="FeePlanner.Plan"/>. An empty list says the NFT holds
	/// nothing. <c>PhantasmaAPI.Fees.PlanAsync</c> reads it from the chain when it is not given.
	/// </para>
	/// </summary>
	public IReadOnlyList<InfusedAsset>? Infusions { get; set; }
	/// <inheritdoc cref="NativeFeeParams.ScriptUnitsAllowance"/>
	public ulong? ScriptUnitsAllowance { get; set; }
	/// <inheritdoc cref="NativeFeeParams.ScriptEventBytes"/>
	public uint? ScriptEventBytes { get; set; }
	/// <inheritdoc cref="NativeFeeParams.ScriptStorageQuanta"/>
	public uint? ScriptStorageQuanta { get; set; }

	/// <summary>
	/// How many witnesses will sign a Call / Call_Multi / Trade / Phantasma message. Required for
	/// those types and for them only: their witness set is chosen by the caller, nothing in the
	/// message says how large it will be, and each witness adds 96 bytes the chain bills. Every other
	/// type fixes its own witness set, so passing this for one of them is an error rather than a hint.
	/// <c>PhantasmaAPI.SendTransactionAsync</c> fills it in from the signers it was given.
	/// </summary>
	public int? WitnessCount { get; set; }

	/// <summary>Returns a shallow copy of the same runtime type. A caller fills in a field on the copy
	/// and leaves the original untouched.</summary>
	public FeePlanOptions Clone() => (FeePlanOptions)MemberwiseClone();
}

/// <summary>A fee plan for one message: the estimate, what it was computed from, and how to apply it.</summary>
public sealed class FeePlan
{
	/// <summary>
	/// The operations the message was recognised as, in call order. The bill was computed from them. An
	/// ordinary message has one entry. A <c>Call_Multi</c> has one entry per inner call.
	/// <para>
	/// Every kind except <see cref="NativeFeeKind.Script"/> is priced with the chain's own formula for
	/// that operation. <c>Script</c> covers VM scripts and unmodelled calls. Their work depends on
	/// execution, so they can only be budgeted (see
	/// <see cref="FeePlanOptions.ScriptUnitsAllowance"/> and its neighbours).
	/// </para>
	/// <para>
	/// This property says what was priced. How firm the number is, <see cref="Exact"/> answers. That
	/// property accounts for both causes: a budgeted part, and a state fact the plan had to assume.
	/// </para>
	/// </summary>
	public IReadOnlyList<NativeFeeKind> Kinds { get; }

	/// <summary>
	/// True when this bill is a prediction of the settlement. False when it is an upper bound on it. A
	/// wallet showing a fee reads this one property to choose between "0.0073 KCAL" and "up to 0.0073
	/// KCAL".
	/// <para>
	/// The property is false in two cases. A chain-state fact was left unstated and its assumed reading
	/// decided part of the price. Or some part of the message had to be budgeted.
	/// </para>
	/// <para>
	/// The property says nothing about facts the caller stated. A stated fact that is wrong produces a
	/// wrong bill, and the transaction then aborts if the mistake was on the cheap side. <c>true</c>
	/// means only that the plan did not have to guess.
	/// </para>
	/// <para>
	/// <c>BigFungible = true</c> is the one exception. It claims nothing about chain state. It asks the
	/// model to price the widest answer a variable-length balance can have. A plan that rests on it
	/// reports <c>false</c> however it was arrived at.
	/// </para>
	/// </summary>
	public bool Exact { get; }

	/// <summary>The signed size the plan was computed for. These are the bytes the block will
	/// carry.</summary>
	public int EnvelopeBytes { get; }

	/// <inheritdoc cref="FeeQuote.MaxGas"/>
	public ulong MaxGas => Estimate.MaxGas;
	/// <inheritdoc cref="FeeQuote.MaxData"/>
	public ulong MaxData => Estimate.MaxData;
	/// <inheritdoc cref="FeeQuote.ExpectedGasBill"/>
	public ulong ExpectedGasBill => Estimate.ExpectedGasBill;
	/// <inheritdoc cref="NativeFeeEstimate.NewStorageQuanta"/>
	public uint NewStorageQuanta => Estimate.NewStorageQuanta;
	/// <inheritdoc cref="NativeFeeEstimate.DeletedStorageQuanta"/>
	public uint DeletedStorageQuanta => Estimate.DeletedStorageQuanta;

	/// <summary>The underlying Tier-1 estimate.</summary>
	public NativeFeeEstimate Estimate { get; }

	public FeePlan(IReadOnlyList<NativeFeeKind> kinds, bool exact, int envelopeBytes, NativeFeeEstimate estimate)
	{
		Kinds = kinds;
		Exact = exact;
		EnvelopeBytes = envelopeBytes;
		Estimate = estimate;
	}

	/// <summary>A copy of <paramref name="msg"/> with <c>maxGas</c> and <c>maxData</c> set to the plan. The input is left untouched.</summary>
	public TxMsg Apply(TxMsg msg)
	{
		msg.maxGas = MaxGas;
		msg.maxData = MaxData;
		return msg;
	}
}

/// <summary>
/// Plans the gas offer and the storage ceiling of a message from the message itself. Its type and
/// contents decide the operation model. Its signed size is computed with placeholder witnesses. The
/// chain config supplies the prices.
/// <para>
/// The method touches no network. Fetch the config with <c>PhantasmaAPI.Fees</c> or getGasConfig and
/// pass it in.
/// </para>
/// </summary>
public static class FeePlanner
{
	public static FeePlan Plan(in TxMsg msg, in GasConfig config, FeePlanOptions? options = null)
	{
		options ??= new FeePlanOptions();
		// A witness-array message does not say how many signatures it will carry, and each one is 96
		// billed bytes. An assumed single witness would under-offer every multi-party transaction by 96
		// bytes per extra signature, and the chain would reject it. So the count is demanded here. The
		// calculator takes the same stance on a missing envelope size.
		if (SignedTxMsg.RequiredWitnesses(msg) == null && !options.WitnessCount.HasValue)
			throw new ArgumentException($"{msg.type} transactions choose their own witnesses: pass WitnessCount to plan one", nameof(options));
		var parts = Describe(msg, options);
		var envelopeBytes = SignedTxMsg.EnvelopeBytes(msg, options.WitnessCount);
		var estimate = NativeFeeEstimator.EstimateNativeFeeBatch(
			parts, config, new NativeFeeTransaction { EnvelopeBytes = (uint)envelopeBytes });
		var budgeted = parts.Any(part => part.Kind == NativeFeeKind.Script);
		var exact = !budgeted && !AssumptionsMattered(msg, config, options, envelopeBytes, estimate);
		return new FeePlan(parts.Select(part => part.Kind).ToArray(), exact, envelopeBytes, estimate);
	}

	/// <summary>
	/// Returns true if a fact the caller left unstated changed this quote.
	/// <para>
	/// The message is priced a second time, with every unstated state fact at its CHEAPER reading. The
	/// two quotes are then compared. If they agree, the costlier defaults decided nothing and the bill
	/// is a prediction.
	/// </para>
	/// <para>
	/// The question is asked this way to keep ONE definition of which facts an operation reads: the
	/// operation models themselves. A list written here would drift from them as the models change.
	/// </para>
	/// <para>
	/// The answer is also per message, not per kind. That matters. A gas-token transfer does not depend
	/// on <c>RecipientHoldsToken</c> at all, because the chain's own rows are free. A plan that
	/// reported the fact as assumed would send every ordinary transfer to the "up to" branch.
	/// </para>
	/// <para>
	/// <c>Infusions</c> is not flipped. It has no cheaper reading, and the planner demands it.
	/// </para>
	/// <para>
	/// <c>BigFungible</c> is flipped even when the caller stated it. The other facts claim something
	/// about chain state, and a true claim yields an exact price. This one asks the model to price the
	/// WIDEST answer a variable-length balance can have: 33 bytes, against a measured 10 at a 2^70
	/// balance. A plan resting on that is a bound for whoever asked for it. The live matrix caught this
	/// as two rows that called themselves predictions and settled below their own number.
	/// </para>
	/// </summary>
	private static bool AssumptionsMattered(in TxMsg msg, in GasConfig config, FeePlanOptions options, int envelopeBytes, NativeFeeEstimate quoted)
	{
		var cheapest = options.Clone();
		cheapest.RecipientHoldsToken = options.RecipientHoldsToken ?? true;
		cheapest.TokenBurnedBefore = options.TokenBurnedBefore ?? true;
		cheapest.SupplyRowExists = options.SupplyRowExists ?? true;
		cheapest.BigFungible = false;
		cheapest.RomHasMetaId = options.RomHasMetaId ?? false;
		cheapest.SeriesHasMetaId = options.SeriesHasMetaId ?? false;
		cheapest.DuplicatedSeries = options.DuplicatedSeries ?? false;
		var cheaper = NativeFeeEstimator.EstimateNativeFeeBatch(
			Describe(msg, cheapest), config, new NativeFeeTransaction { EnvelopeBytes = (uint)envelopeBytes });
		return cheaper.ExpectedGasBill != quoted.ExpectedGasBill
			|| cheaper.MaxGas != quoted.MaxGas
			|| cheaper.MaxData != quoted.MaxData;
	}

	/// <summary>
	/// Returns the NFT instances a message burns. It covers the native burn types, a
	/// <c>Token.BurnNonFungible</c> call, and every such call inside a <c>Call_Multi</c>.
	/// <para>
	/// A burn returns whatever the instance's own address holds, and the chain charges for each
	/// returned asset. A planner with a chain to ask reads those assets per instance and hands the
	/// union to <see cref="FeePlanOptions.Infusions"/>.
	/// </para>
	/// <para>
	/// This method sits beside the decomposition that decides which calls are burns, so the two cannot
	/// come to disagree.
	/// </para>
	/// </summary>
	public static IReadOnlyList<(UInt64 TokenId, UInt64 InstanceId)> BurnedInstances(in TxMsg msg)
	{
		switch (msg.type)
		{
			case TxTypes.BurnNonFungible:
				{
					var inner = (TxMsgBurnNonFungible)msg.msg;
					return new[] { (inner.tokenId, inner.instanceId) };
				}
			case TxTypes.BurnNonFungible_GasPayer:
				{
					var inner = (TxMsgBurnNonFungible_GasPayer)msg.msg;
					return new[] { (inner.tokenId, inner.instanceId) };
				}
			case TxTypes.Call:
				return BurnedByCall((TxMsgCall)msg.msg);
			case TxTypes.Call_Multi:
				// A batch with no call array burns nothing, the same reading Describe takes of it.
				return (((TxMsgCall_Multi)msg.msg).calls ?? Array.Empty<TxMsgCall>()).SelectMany(BurnedByCall).ToArray();
			default:
				return Array.Empty<(UInt64, UInt64)>();
		}
	}

	private static IReadOnlyList<(UInt64 TokenId, UInt64 InstanceId)> BurnedByCall(TxMsgCall call)
	{
		if (call.sections.HasSections
			|| call.moduleId != (uint)ModuleId.Token
			|| call.methodId != (uint)TokenContract_Methods.BurnNonFungible)
			return Array.Empty<(UInt64, UInt64)>();
		var args = CarbonBlob.New<BurnNonFungibleArgs>(call.args);
		return args.instanceIds.Select(id => (args.tokenId, id)).ToArray();
	}

	// What a message does, written as the calculator's operations. An ordinary message gives one entry.
	// A Call_Multi gives one entry per inner call. That is what lets a batch be priced.
	private static IReadOnlyList<NativeFeePart> Describe(in TxMsg msg, FeePlanOptions options)
	{
		// Passed through even when unset. The calculator owns every default, so no default is decided
		// in two places. Whether the recipient is an NFT-derived address is NOT here. The address form
		// decides that, and the message carries the address, so each branch below reads it out.
		NativeFeeParams StateFacts() => new()
		{
			RecipientHoldsToken = options.RecipientHoldsToken ?? false,
			BigFungible = options.BigFungible,
			TokenBurnedBefore = options.TokenBurnedBefore ?? false,
			SupplyRowExists = options.SupplyRowExists ?? false,
			RomHasMetaId = options.RomHasMetaId
		};

		switch (msg.type)
		{
			case TxTypes.TransferFungible:
				{
					var inner = (TxMsgTransferFungible)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.TransferFungible, p);
				}
			case TxTypes.TransferFungible_GasPayer:
				{
					var inner = (TxMsgTransferFungible_GasPayer)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.TransferFungible, p);
				}
			case TxTypes.TransferNonFungible_Single:
				{
					var inner = (TxMsgTransferNonFungible_Single)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = 1;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.TransferNonFungible_Single_GasPayer:
				{
					var inner = (TxMsgTransferNonFungible_Single_GasPayer)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = 1;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.TransferNonFungible_Multi:
				{
					var inner = (TxMsgTransferNonFungible_Multi)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = (uint)inner.instanceIds.Length;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.TransferNonFungible_Multi_GasPayer:
				{
					var inner = (TxMsgTransferNonFungible_Multi_GasPayer)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = (uint)inner.instanceIds.Length;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.MintFungible:
				{
					var inner = (TxMsgMintFungible)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.MintFungible, p);
				}
			case TxTypes.BurnFungible:
				{
					var p = StateFacts();
					p.TokenId = ((TxMsgBurnFungible)msg.msg).tokenId;
					return One(NativeFeeKind.BurnFungible, p);
				}
			case TxTypes.BurnFungible_GasPayer:
				{
					var p = StateFacts();
					p.TokenId = ((TxMsgBurnFungible_GasPayer)msg.msg).tokenId;
					return One(NativeFeeKind.BurnFungible, p);
				}
			case TxTypes.MintNonFungible:
				{
					var inner = (TxMsgMintNonFungible)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.RomBytes = (uint)inner.rom.Length;
					p.RamBytes = (uint)inner.ram.Length;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return One(NativeFeeKind.MintNonFungible, p);
				}
			case TxTypes.BurnNonFungible:
			case TxTypes.BurnNonFungible_GasPayer:
				{
					// The stored ROM is chain state that the message does not carry, so the deleted quanta
					// are a lower bound. The offer is unaffected. A burn deletes more rows than it
					// creates, and only the rows it creates are escrowed.
					var p = StateFacts();
					p.TokenId = msg.type == TxTypes.BurnNonFungible
						? ((TxMsgBurnNonFungible)msg.msg).tokenId
						: ((TxMsgBurnNonFungible_GasPayer)msg.msg).tokenId;
					p.Count = 1;
					p.Infusions = RequireInfusions(options.Infusions);
					return One(NativeFeeKind.BurnNonFungible, p);
				}
			case TxTypes.Call:
				return new[] { DescribeCall((TxMsgCall)msg.msg, options, StateFacts, options.Infusions) };
			case TxTypes.Call_Multi:
				{
					// The chain runs the calls in a loop and bills their sum, so the plan is the sum of
					// their models. Infusions cover every burn in the batch. The returns cost the same
					// wherever they are counted, so the first burn takes the whole list and the burns
					// after it take none.
					var calls = ((TxMsgCall_Multi)msg.msg).calls ?? Array.Empty<TxMsgCall>();
					var parts = new List<NativeFeePart>(calls.Length);
					var returns = options.Infusions;
					foreach (var call in calls)
					{
						var part = DescribeCall(call, options, StateFacts, returns);
						if (part.Kind == NativeFeeKind.BurnNonFungible)
							returns = Array.Empty<InfusedAsset>();
						parts.Add(part);
					}
					return parts;
				}
			case TxTypes.Trade:
			case TxTypes.Phantasma:
				return new[] { ScriptPlan(options) };
			default:
				throw new ArgumentException($"Cannot plan fees for transaction type {msg.type}", nameof(msg));
		}
	}

	private static IReadOnlyList<NativeFeePart> One(NativeFeeKind kind, NativeFeeParams p) =>
		new[] { new NativeFeePart(kind, p) };

	// What the NFTs hold is chain state, and it has no costlier bound, so the planner demands it. A
	// burn planned as if the addresses were empty is short by every returned asset. It then aborts and
	// is billed, on every retry.
	private static IReadOnlyList<InfusedAsset> RequireInfusions(IReadOnlyList<InfusedAsset>? infusions)
	{
		if (infusions == null)
			throw new ArgumentException("A burn returns whatever the NFT holds: pass Infusions (empty when it holds nothing) or plan through PhantasmaAPI.Fees, which reads them from the chain", nameof(infusions));
		return infusions;
	}

	private static NativeFeePart DescribeCall(TxMsgCall call, FeePlanOptions options, Func<NativeFeeParams> stateFacts, IReadOnlyList<InfusedAsset>? infusions)
	{
		// A call can build its arguments at execution time from the results of earlier calls. Such a
		// call carries none of them yet. There is nothing to read a price from, so it takes the script
		// budget.
		if (call.sections.HasSections)
			return ScriptPlan(options);
		if (call.moduleId == (uint)ModuleId.Token)
		{
			switch ((TokenContract_Methods)call.methodId)
			{
				// The five token movements below cost exactly what they cost as native transaction
				// types. Both paths enter the same contract method. They arrive as module calls
				// because a wallet batched them. Token.MintNonFungible is absent on purpose: the chain
				// refuses it under mainnet SR 50 whichever way it arrives, so there is nothing to
				// price.
				case TokenContract_Methods.TransferFungible:
					{
						var args = CarbonBlob.New<TransferFungibleArgs>(call.args);
						var p = stateFacts();
						p.TokenId = args.tokenId;
						p.ToIsNftAddress = TokenHelper.IsNftAddress(args.to);
						return new NativeFeePart(NativeFeeKind.TransferFungible, p);
					}
				case TokenContract_Methods.TransferNonFungible:
					{
						var args = CarbonBlob.New<TransferNonFungibleArgs>(call.args);
						var p = stateFacts();
						p.TokenId = args.tokenId;
						p.Count = (uint)args.instanceIds.Length;
						p.ToIsNftAddress = TokenHelper.IsNftAddress(args.to);
						return new NativeFeePart(NativeFeeKind.TransferNonFungible, p);
					}
				case TokenContract_Methods.MintFungible:
					{
						var args = CarbonBlob.New<MintFungibleArgs>(call.args);
						var p = stateFacts();
						p.TokenId = args.tokenId;
						p.ToIsNftAddress = TokenHelper.IsNftAddress(args.to);
						return new NativeFeePart(NativeFeeKind.MintFungible, p);
					}
				case TokenContract_Methods.BurnFungible:
					{
						var p = stateFacts();
						p.TokenId = CarbonBlob.New<BurnFungibleArgs>(call.args).tokenId;
						return new NativeFeePart(NativeFeeKind.BurnFungible, p);
					}
				case TokenContract_Methods.BurnNonFungible:
					{
						var args = CarbonBlob.New<BurnNonFungibleArgs>(call.args);
						var p = stateFacts();
						p.TokenId = args.tokenId;
						p.Count = (uint)args.instanceIds.Length;
						p.Infusions = RequireInfusions(infusions);
						return new NativeFeePart(NativeFeeKind.BurnNonFungible, p);
					}
				case TokenContract_Methods.CreateToken:
					{
						var info = CarbonBlob.New<TokenInfo>(call.args);
						var metadata = info.metadata != null && info.metadata.Length > 0
							? CarbonBlob.New<VmDynamicStruct>(info.metadata)
							: (VmDynamicStruct?)null;
						// The token-info row is the Call arguments as submitted. The chain stores the
						// TokenInfo it was given, metadata included, and measured bills confirm that the row
						// equals the arguments. The metadata decides which extra rows the creation writes and
						// which lookups its validation costs. The plan can read the metadata, because it is a
						// named struct.
						return new NativeFeePart(NativeFeeKind.CreateToken, new NativeFeeParams
						{
							SymbolLength = info.symbol.data?.Length ?? 0,
							TokenInfoBytes = (uint)call.args.Length,
							NonFungible = (info.flags & TokenFlags.NonFungible) != 0,
							HasPreBurn = metadata?.GetValue(StandardMeta.Token.pre_burn) != null,
							HasInflationSchedule = metadata?.GetValue(StandardMeta.Token.inflation_period) != null,
							HasStakingOrganisation = metadata?.GetValue(StandardMeta.Token.staking_org_id) != null,
							HasStakingRewardToken = metadata?.GetValue(StandardMeta.Token.staking_reward_token) != null
						});
					}
				case TokenContract_Methods.CreateTokenSeries:
					// The arguments are the u64 token id followed by the SeriesInfo, which becomes the row.
					return new NativeFeePart(NativeFeeKind.CreateTokenSeries, new NativeFeeParams
					{
						SeriesInfoBytes = (uint)Math.Max(call.args.Length - 8, 0),
						SeriesHasMetaId = options.SeriesHasMetaId
					});
				case TokenContract_Methods.MintPhantasmaNonFungible:
					{
						var args = CarbonBlob.New<MintPhantasmaNonFungibleArgs>(call.args);
						// Each instance names the series it is minted into. The number of distinct series a
						// duplicated mint touches is therefore readable from the call, and the caller never has
						// to supply it. The chain's per-series supply reads are charged per distinct series.
						var seriesIds = new HashSet<BigInteger>();
						var roms = new uint[args.tokens.Length];
						var rams = new uint[args.tokens.Length];
						for (var i = 0; i < args.tokens.Length; i++)
						{
							seriesIds.Add((BigInteger)args.tokens[i].phantasmaSeriesId);
							roms[i] = (uint)args.tokens[i].rom.Length;
							rams[i] = (uint)args.tokens[i].ram.Length;
						}
						var p = stateFacts();
						p.TokenId = args.tokenId;
						p.Count = (uint)args.tokens.Length;
						p.RomBytesPerInstance = roms;
						p.RamBytesPerInstance = rams;
						p.DuplicatedSeries = options.DuplicatedSeries;
						p.DistinctSeriesCount = (uint)seriesIds.Count;
						p.ToIsNftAddress = TokenHelper.IsNftAddress(args.address);
						return new NativeFeePart(NativeFeeKind.MintPhantasmaNonFungible, p);
					}
				default:
					return ScriptPlan(options);
			}
		}
		if (call.moduleId == (uint)ModuleId.Governance && call.methodId == (uint)GovernanceContract_Methods.RegisterName)
		{
			// The arguments are the 32-byte address followed by the name as a SmallString.
			using var stream = new MemoryStream(call.args);
			using var reader = new BinaryReader(stream);
			reader.Read32(out Bytes32 _);
			reader.Read(out SmallString name);
			return new NativeFeePart(NativeFeeKind.RegisterName, new NativeFeeParams { NameLength = name.data?.Length ?? 0 });
		}
		return ScriptPlan(options);
	}

	private static NativeFeePart ScriptPlan(FeePlanOptions options)
	{
		var p = new NativeFeeParams();
		if (options.ScriptUnitsAllowance.HasValue) p.ScriptUnitsAllowance = options.ScriptUnitsAllowance.Value;
		if (options.ScriptEventBytes.HasValue) p.ScriptEventBytes = options.ScriptEventBytes.Value;
		if (options.ScriptStorageQuanta.HasValue) p.ScriptStorageQuanta = options.ScriptStorageQuanta.Value;
		return new NativeFeePart(NativeFeeKind.Script, p);
	}
}
