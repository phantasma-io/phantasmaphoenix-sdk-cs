using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Facts about chain state and signing that a message does not carry but the fee depends on. Every
/// state fact left null takes the calculator's default, which is the case that costs more, so an
/// unspecified plan is an upper bound the settlement can only undercut; the defaults themselves
/// belong to <see cref="NativeFeeParams"/>, which documents each one. The one fact without a
/// costlier reading is <see cref="Infusions"/>: a burned NFT can hold any number of assets, so the
/// planner demands it instead of assuming, and <c>PhantasmaAPI.Fees</c> reads it from the chain.
/// <para>
/// The facts the message itself carries - counts, sizes, token ids, the NFT-address recipient,
/// <c>nonFungible</c>, <c>pre_burn</c> - are deliberately absent: <see cref="FeePlanner.Plan"/> reads
/// those out of the message, and a caller-supplied value could only contradict it. Every field is
/// optional and most callers set none. Roughly in order of how likely a caller is to know the answer:
/// an ordinary wallet may well know the first four; the next three need the token's schema or the
/// series' metadata; the last three size the allowance for a VM script, whose cost no formula predicts.
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
	/// What the burned NFT holds at its own address (BurnNonFungible), which the burn returns to
	/// the burner at a price - see <see cref="NativeFeeParams.Infusions"/>. Required to plan a burn
	/// through <see cref="FeePlanner.Plan"/>: an empty list says the NFT holds nothing.
	/// <c>PhantasmaAPI.Fees.PlanAsync</c> reads it from the chain when it is not given.
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

	/// <summary>A shallow copy, of the same runtime type, for callers that fill in a field without touching the caller's options.</summary>
	public FeePlanOptions Clone() => (FeePlanOptions)MemberwiseClone();
}

/// <summary>A fee plan for one message: the estimate, what it was computed from, and how to apply it.</summary>
public sealed class FeePlan
{
	/// <summary>
	/// The operations the message was recognised as, in call order, which is also what the bill was
	/// computed from: one entry for an ordinary message, one per inner call for a <c>Call_Multi</c>.
	/// <para>
	/// Every kind but <see cref="NativeFeeKind.Script"/> is priced with the chain's own formula for that
	/// operation; <c>Script</c> covers VM scripts and unmodelled calls, whose work depends on execution
	/// and can only be budgeted (see <see cref="FeePlanOptions.ScriptUnitsAllowance"/> and its neighbours).
	/// So a plan is a prediction when no entry is <c>Script</c>, and a budget in the part that is.
	/// </para>
	/// <para>
	/// A formula-priced bill is exact for the facts it was given and an upper bound for the ones it
	/// had to assume: a state fact left unspecified is filled with its costlier default.
	/// </para>
	/// </summary>
	public IReadOnlyList<NativeFeeKind> Kinds { get; }

	/// <summary>The signed size the plan was computed for - the bytes the block will carry.</summary>
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

	public FeePlan(IReadOnlyList<NativeFeeKind> kinds, int envelopeBytes, NativeFeeEstimate estimate)
	{
		Kinds = kinds;
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
/// Plans the gas offer and storage ceiling of a message from the message itself: its type and
/// contents decide the operation model, its signed size is computed with placeholder witnesses, and
/// the chain config supplies the prices. Pure - fetch the config with <c>PhantasmaAPI.Fees</c> or
/// getGasConfig and pass it in.
/// </summary>
public static class FeePlanner
{
	public static FeePlan Plan(in TxMsg msg, in GasConfig config, FeePlanOptions? options = null)
	{
		options ??= new FeePlanOptions();
		// A witness-array message does not say how many signatures it will carry, and each one is 96
		// billed bytes. Assuming a single witness would under-offer every multi-party transaction by 96
		// bytes each and get it rejected, so the count is demanded rather than guessed - the same stance
		// the calculator takes on a missing envelope size.
		if (SignedTxMsg.RequiredWitnesses(msg) == null && !options.WitnessCount.HasValue)
			throw new ArgumentException($"{msg.type} transactions choose their own witnesses: pass WitnessCount to plan one", nameof(options));
		var parts = Describe(msg, options);
		var envelopeBytes = SignedTxMsg.EnvelopeBytes(msg, options.WitnessCount);
		var estimate = NativeFeeEstimator.EstimateNativeFeeBatch(
			parts, config, new NativeFeeTransaction { EnvelopeBytes = (uint)envelopeBytes });
		return new FeePlan(parts.Select(part => part.Kind).ToArray(), envelopeBytes, estimate);
	}

	/// <summary>
	/// The NFT instances a message burns: the native burn types, a <c>Token.BurnNonFungible</c> call,
	/// and every such call inside a <c>Call_Multi</c>. A burn returns whatever the instance's own
	/// address holds and pays for each returned asset, so a planner with a chain to ask reads them
	/// per instance and hands the union to <see cref="FeePlanOptions.Infusions"/>. It lives beside
	/// the decomposition that decides which calls are burns so the two cannot come to disagree.
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

	// What a message does, as the calculator's operations. One entry for an ordinary message; a
	// Call_Multi yields one per inner call, which is what lets a batch be priced instead of budgeted.
	private static IReadOnlyList<NativeFeePart> Describe(in TxMsg msg, FeePlanOptions options)
	{
		// Passed through unset and all: the calculator owns every default, so no default is decided
		// in two places. Whether the recipient is an NFT-derived address is NOT here: the address form
		// decides it, and the message carries the address, so each branch below reads it out.
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
					// The stored ROM is chain state the message does not carry, so the deleted quanta are
					// a lower bound. That does not touch the offer: a burn deletes more than it creates,
					// and only the rows it creates are escrowed.
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
					// their models. Infusions cover every burn in the batch and the returns cost the same
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

	// What the NFTs hold is chain state with no costlier bound, so it is demanded, not assumed: a
	// burn planned as if the addresses were empty is short by every returned asset and aborts,
	// billed, on every retry.
	private static IReadOnlyList<InfusedAsset> RequireInfusions(IReadOnlyList<InfusedAsset>? infusions)
	{
		if (infusions == null)
			throw new ArgumentException("A burn returns whatever the NFT holds: pass Infusions (empty when it holds nothing) or plan through PhantasmaAPI.Fees, which reads them from the chain", nameof(infusions));
		return infusions;
	}

	private static NativeFeePart DescribeCall(TxMsgCall call, FeePlanOptions options, Func<NativeFeeParams> stateFacts, IReadOnlyList<InfusedAsset>? infusions)
	{
		// A call whose arguments are assembled at execution out of earlier calls' results carries none
		// of them yet. There is nothing to read a price from, so it is budgeted like any unmodelled call.
		if (call.sections.HasSections)
			return ScriptPlan(options);
		if (call.moduleId == (uint)ModuleId.Token)
		{
			switch ((TokenContract_Methods)call.methodId)
			{
				// The five token movements below cost exactly what they cost as native transaction
				// types: both paths enter the same contract method, and a batched wallet operation is
				// the reason they arrive as module calls at all. Token.MintNonFungible is deliberately
				// absent - the chain refuses it under mainnet SR 50 whichever way it arrives, so there
				// is nothing to price.
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
						// The token-info row is the Call arguments as submitted: the chain stores the
						// TokenInfo it was given, metadata included, and measured bills confirm the row equals
						// the arguments. Which extra rows the creation writes, and which lookups validating it
						// costs, is decided by the metadata, which is a named struct the plan can read.
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
						// Each instance names the series it is minted into, so the number of distinct series a
						// duplicated mint touches - which is what the chain's per-series supply read costs
						// follow - is readable from the call and never has to be supplied by the caller.
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
