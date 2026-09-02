using System.Numerics;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Facts about chain state and signing that a message does not carry but the fee depends on. Every
/// state fact left null takes the calculator's default, which is the case that costs more, so an
/// unspecified plan is an upper bound the settlement can only undercut; the defaults themselves
/// belong to <see cref="NativeFeeParams"/>, which documents each one.
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
}

/// <summary>A fee plan for one message: the estimate, what it was computed from, and how to apply it.</summary>
public sealed class FeePlan
{
	/// <summary>
	/// The operation the message was recognised as, which is also what the bill was computed from.
	/// Every kind but <see cref="NativeFeeKind.Script"/> is priced with the chain's own formula for that
	/// operation; <c>Script</c> covers VM scripts and unmodelled calls, whose work depends on execution
	/// and can only be budgeted (see <see cref="FeePlanOptions.ScriptUnitsAllowance"/> and its neighbours).
	/// <para>
	/// A formula-priced bill is exact for the facts it was given and an upper bound for the ones it
	/// had to assume: a state fact left unspecified is filled with its costlier default.
	/// </para>
	/// </summary>
	public NativeFeeKind Kind { get; }

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

	public FeePlan(NativeFeeKind kind, int envelopeBytes, NativeFeeEstimate estimate)
	{
		Kind = kind;
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
		var (kind, p) = Describe(msg, options);
		var envelopeBytes = SignedTxMsg.EnvelopeBytes(msg, options.WitnessCount);
		p.EnvelopeBytes = (uint)envelopeBytes;
		return new FeePlan(kind, envelopeBytes, NativeFeeEstimator.EstimateNativeFee(kind, config, p));
	}

	private static (NativeFeeKind, NativeFeeParams) Describe(in TxMsg msg, FeePlanOptions options)
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
					return (NativeFeeKind.TransferFungible, p);
				}
			case TxTypes.TransferFungible_GasPayer:
				{
					var inner = (TxMsgTransferFungible_GasPayer)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.TransferFungible, p);
				}
			case TxTypes.TransferNonFungible_Single:
				{
					var inner = (TxMsgTransferNonFungible_Single)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = 1;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.TransferNonFungible_Single_GasPayer:
				{
					var inner = (TxMsgTransferNonFungible_Single_GasPayer)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = 1;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.TransferNonFungible_Multi:
				{
					var inner = (TxMsgTransferNonFungible_Multi)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = (uint)inner.instanceIds.Length;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.TransferNonFungible_Multi_GasPayer:
				{
					var inner = (TxMsgTransferNonFungible_Multi_GasPayer)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.Count = (uint)inner.instanceIds.Length;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.TransferNonFungible, p);
				}
			case TxTypes.MintFungible:
				{
					var inner = (TxMsgMintFungible)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.MintFungible, p);
				}
			case TxTypes.BurnFungible:
				{
					var p = StateFacts();
					p.TokenId = ((TxMsgBurnFungible)msg.msg).tokenId;
					return (NativeFeeKind.BurnFungible, p);
				}
			case TxTypes.BurnFungible_GasPayer:
				{
					var p = StateFacts();
					p.TokenId = ((TxMsgBurnFungible_GasPayer)msg.msg).tokenId;
					return (NativeFeeKind.BurnFungible, p);
				}
			case TxTypes.MintNonFungible:
				{
					var inner = (TxMsgMintNonFungible)msg.msg;
					var p = StateFacts();
					p.TokenId = inner.tokenId;
					p.RomBytes = (uint)inner.rom.Length;
					p.RamBytes = (uint)inner.ram.Length;
					p.ToIsNftAddress = TokenHelper.IsNftAddress(inner.to);
					return (NativeFeeKind.MintNonFungible, p);
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
					return (NativeFeeKind.BurnNonFungible, p);
				}
			case TxTypes.Call:
				return DescribeCall((TxMsgCall)msg.msg, options, StateFacts);
			case TxTypes.Call_Multi:
			case TxTypes.Trade:
			case TxTypes.Phantasma:
				return ScriptPlan(options);
			default:
				throw new ArgumentException($"Cannot plan fees for transaction type {msg.type}", nameof(msg));
		}
	}

	private static (NativeFeeKind, NativeFeeParams) DescribeCall(TxMsgCall call, FeePlanOptions options, Func<NativeFeeParams> stateFacts)
	{
		if (call.moduleId == (uint)ModuleId.Token)
		{
			switch ((TokenContract_Methods)call.methodId)
			{
				case TokenContract_Methods.CreateToken:
					{
						var info = CarbonBlob.New<TokenInfo>(call.args);
						var metadata = info.metadata != null && info.metadata.Length > 0
							? CarbonBlob.New<VmDynamicStruct>(info.metadata)
							: (VmDynamicStruct?)null;
						// The token-info row is the Call arguments as submitted: the chain stores the
						// TokenInfo it was given, metadata included, and measured bills confirm the row equals
						// the arguments. Which extra rows the creation writes is decided by the metadata, which
						// is a named struct the plan can read.
						return (NativeFeeKind.CreateToken, new NativeFeeParams
						{
							SymbolLength = info.symbol.data?.Length ?? 0,
							TokenInfoBytes = (uint)call.args.Length,
							NonFungible = (info.flags & TokenFlags.NonFungible) != 0,
							HasPreBurn = metadata?.GetValue(StandardMeta.Token.pre_burn) != null,
							HasInflationSchedule = metadata?.GetValue(StandardMeta.Token.inflation_period) != null
						});
					}
				case TokenContract_Methods.CreateTokenSeries:
					// The arguments are the u64 token id followed by the SeriesInfo, which becomes the row.
					return (NativeFeeKind.CreateTokenSeries, new NativeFeeParams
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
						return (NativeFeeKind.MintPhantasmaNonFungible, p);
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
			return (NativeFeeKind.RegisterName, new NativeFeeParams { NameLength = name.data?.Length ?? 0 });
		}
		return ScriptPlan(options);
	}

	private static (NativeFeeKind, NativeFeeParams) ScriptPlan(FeePlanOptions options)
	{
		var p = new NativeFeeParams();
		if (options.ScriptUnitsAllowance.HasValue) p.ScriptUnitsAllowance = options.ScriptUnitsAllowance.Value;
		if (options.ScriptEventBytes.HasValue) p.ScriptEventBytes = options.ScriptEventBytes.Value;
		if (options.ScriptStorageQuanta.HasValue) p.ScriptStorageQuanta = options.ScriptStorageQuanta.Value;
		return (NativeFeeKind.Script, p);
	}
}
