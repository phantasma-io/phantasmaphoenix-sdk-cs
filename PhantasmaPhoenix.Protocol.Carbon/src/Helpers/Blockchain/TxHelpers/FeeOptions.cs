namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

/// <summary>
/// Common interface for fee option.
/// </summary>
public interface IFeeOptions
{
	ulong FeeMultiplier { get; }
	ulong CalculateMaxGas(params object[] args);
}

/// <summary>
/// Base fee options with sensible defaults.
/// </summary>
public class FeeOptions : IFeeOptions
{
	public ulong GasFeeBase { get; }
	public ulong FeeMultiplier { get; }

	public FeeOptions(ulong gasFeeBase = 10_000UL, ulong feeMultiplier = 1_000UL)
	{
		GasFeeBase = gasFeeBase;
		FeeMultiplier = feeMultiplier;
	}

	public virtual ulong CalculateMaxGas(params object[] args)
	{
		return GasFeeBase * FeeMultiplier;
	}
}

/// <summary>
/// Fee options for token creation transactions.
/// Inherits defaults from <see cref="FeeOptions"/>.
/// </summary>
public class CreateTokenFeeOptions : FeeOptions, IFeeOptions
{
	public ulong GasFeeCreateTokenBase { get; }
	public ulong GasFeeCreateTokenSymbol { get; }

	public CreateTokenFeeOptions(
		ulong gasFeeBase = 10_000UL,
		ulong gasFeeCreateTokenBase = 10_000_000_000UL,
		ulong gasFeeCreateTokenSymbol = 10_000_000_000UL,
		ulong feeMultiplier = 10_000UL): base(gasFeeBase, feeMultiplier)
	{
		GasFeeCreateTokenBase = gasFeeCreateTokenBase;
		GasFeeCreateTokenSymbol = gasFeeCreateTokenSymbol;
	}

	public override ulong CalculateMaxGas(params object[] args)
	{
		string symbol = args.Length > 0 && args[0] is SmallString s ? s.data : "";
		int symbolLen = symbol.Length;
		return (GasFeeBase + GasFeeCreateTokenBase + (GasFeeCreateTokenSymbol >> (symbolLen > 0 ? symbolLen - 1 : 0))) * FeeMultiplier;
	}
}

/// <summary>
/// Fee options for creating a new series on an NFT token.
/// </summary>
public class CreateSeriesFeeOptions : FeeOptions, IFeeOptions
{
	public ulong GasFeeCreateSeriesBase { get; }

	public CreateSeriesFeeOptions(
		ulong gasFeeBase = 10_000UL,
		ulong gasFeeCreateSeriesBase = 2_500_000_000UL,
		ulong feeMultiplier = 10_000UL): base(gasFeeBase, feeMultiplier)
	{
		GasFeeCreateSeriesBase = gasFeeCreateSeriesBase;
	}

	public override ulong CalculateMaxGas(params object[] args)
	{
		return (GasFeeBase + GasFeeCreateSeriesBase) * FeeMultiplier;
	}
}

/// <summary>
/// Fee options for minting non-fungible tokens (NFT instances).
/// </summary>
public class MintNftFeeOptions : FeeOptions, IFeeOptions
{
	public MintNftFeeOptions(ulong gasFeeBase = 10_000UL, ulong feeMultiplier = 1_000UL)
		: base(gasFeeBase, feeMultiplier) { }

	public override ulong CalculateMaxGas(params object[] args)
	{
		return GasFeeBase * FeeMultiplier;
	}
}
