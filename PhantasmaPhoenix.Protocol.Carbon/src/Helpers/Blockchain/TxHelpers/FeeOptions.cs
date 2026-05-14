using System.Collections;
using System.Numerics;

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
		var count = ParseOptionalCount(args, nameof(FeeOptions) + "." + nameof(CalculateMaxGas));
		return MultiplyChecked(GasFeeBase, FeeMultiplier, count);
	}

	protected static ulong ParseOptionalCount(object[] args, string methodName)
	{
		if (args.Length == 0)
		{
			return 1;
		}

		if (args.Length > 1)
		{
			throw new ArgumentException($"{methodName} accepts at most one argument.", nameof(args));
		}

		return ParsePositiveCount(args[0], methodName);
	}

	protected static ulong ParseOptionalMintCount(object[] args, string methodName)
	{
		if (args.Length == 0)
		{
			return 1;
		}

		if (args.Length > 1)
		{
			throw new ArgumentException($"{methodName} accepts at most one argument.", nameof(args));
		}

		var value = args[0];
		if (value is Array array)
		{
			return ParsePositiveCount(array.LongLength, methodName);
		}

		if (value is ICollection collection)
		{
			return ParsePositiveCount(collection.Count, methodName);
		}

		return ParsePositiveCount(value, methodName);
	}

	protected static void AssertNoMeaningfulCount(object[] args, string methodName)
	{
		var count = ParseOptionalCount(args, methodName);
		if (count != 1)
		{
			throw new ArgumentOutOfRangeException(nameof(args), $"{methodName} is not count-sensitive; count must be 1 when provided.");
		}
	}

	protected static ulong MultiplyChecked(ulong gasFeeBase, ulong feeMultiplier, ulong count = 1)
	{
		checked
		{
			return gasFeeBase * feeMultiplier * count;
		}
	}

	private static ulong ParsePositiveCount(object? value, string methodName)
	{
		return value switch
		{
			byte v when v > 0 => v,
			sbyte v when v > 0 => (ulong)v,
			short v when v > 0 => (ulong)v,
			ushort v when v > 0 => v,
			int v when v > 0 => (ulong)v,
			uint v when v > 0 => v,
			long v when v > 0 => (ulong)v,
			ulong v when v > 0 => v,
			BigInteger v when v > 0 && v <= ulong.MaxValue => (ulong)v,
			_ => throw new ArgumentOutOfRangeException(nameof(value), $"{methodName} count must be a positive integer.")
		};
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
		ulong feeMultiplier = 10_000UL) : base(gasFeeBase, feeMultiplier)
	{
		GasFeeCreateTokenBase = gasFeeCreateTokenBase;
		GasFeeCreateTokenSymbol = gasFeeCreateTokenSymbol;
	}

	public override ulong CalculateMaxGas(params object[] args)
	{
		if (args.Length > 1)
		{
			throw new ArgumentException($"{nameof(CreateTokenFeeOptions)}.{nameof(CalculateMaxGas)} accepts at most one argument.", nameof(args));
		}

		string symbol = args.Length == 0 ? "" : args[0] switch
		{
			SmallString s => s.data,
			string s => s,
			_ => throw new ArgumentException($"{nameof(CreateTokenFeeOptions)}.{nameof(CalculateMaxGas)} symbol must be a string or SmallString.", nameof(args))
		};
		int symbolLen = symbol.Length;
		return MultiplyChecked(GasFeeBase + GasFeeCreateTokenBase + (GasFeeCreateTokenSymbol >> (symbolLen > 0 ? symbolLen - 1 : 0)), FeeMultiplier);
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
		ulong feeMultiplier = 10_000UL) : base(gasFeeBase, feeMultiplier)
	{
		GasFeeCreateSeriesBase = gasFeeCreateSeriesBase;
	}

	public override ulong CalculateMaxGas(params object[] args)
	{
		AssertNoMeaningfulCount(args, nameof(CreateSeriesFeeOptions) + "." + nameof(CalculateMaxGas));
		return MultiplyChecked(GasFeeBase + GasFeeCreateSeriesBase, FeeMultiplier);
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
		var count = ParseOptionalMintCount(args, nameof(MintNftFeeOptions) + "." + nameof(CalculateMaxGas));
		return MultiplyChecked(GasFeeBase, FeeMultiplier, count);
	}
}
