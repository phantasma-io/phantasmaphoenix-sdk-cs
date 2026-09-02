using System;
using System.Globalization;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

/// <summary>
/// Response of the getGasConfig RPC method: the current on-chain gas configuration plus the
/// chain parameters fee estimation needs. 64-bit config values are serialized as decimal strings
/// (they can exceed the 2^53 precision of JSON numbers in JavaScript parsers).
/// </summary>
public class GasConfigResult
{
	[ApiDescription("Gas model version: 1 = original fee model, 2 = gas-model-v2 (config version >= 1)")]
	public uint GasModelVersion { get; set; }

	[ApiDescription("Current on-chain GasConfig")]
	public GasConfigDataResult? GasConfig { get; set; }

	[ApiDescription("Chain block rate target in milliseconds")]
	public uint BlockRateTarget { get; set; }

	[ApiDescription("Transaction expiry window in milliseconds")]
	public uint ExpiryWindow { get; set; }

	[ApiDescription("Gas-model-v2 price of block-carried bytes in gas units per byte; null under gas model v1")]
	public uint? UnitsPerBlockDataByte { get; set; }

	public GasConfigResult() { }

	/// <summary>
	/// Converts the JSON model to the wire-format GasConfig struct consumed by the Tier-1 fee
	/// estimator (NativeFeeEstimator). It throws on malformed numeric strings, on a v2 response missing
	/// tail fields, and on a byte price this SDK does not implement. Fees estimated from silently
	/// zeroed or silently stale v2 prices would produce rejected transactions.
	/// </summary>
	public GasConfig ToGasConfig()
	{
		var c = GasConfig ?? throw new InvalidOperationException("GasConfig section is missing");
		var result = new GasConfig
		{
			version = c.Version,
			maxNameLength = c.MaxNameLength,
			maxTokenSymbolLength = c.MaxTokenSymbolLength,
			feeShift = c.FeeShift,
			maxStructureSize = c.MaxStructureSize,
			feeMultiplier = ParseU64(c.FeeMultiplier, nameof(c.FeeMultiplier)),
			gasTokenId = ParseU64(c.GasTokenId, nameof(c.GasTokenId)),
			dataTokenId = ParseU64(c.DataTokenId, nameof(c.DataTokenId)),
			minimumGasOffer = ParseU64(c.MinimumGasOffer, nameof(c.MinimumGasOffer)),
			dataEscrowPerRow = ParseU64(c.DataEscrowPerRow, nameof(c.DataEscrowPerRow)),
			gasFeeTransfer = ParseU64(c.GasFeeTransfer, nameof(c.GasFeeTransfer)),
			gasFeeQuery = ParseU64(c.GasFeeQuery, nameof(c.GasFeeQuery)),
			gasFeeCreateTokenBase = ParseU64(c.GasFeeCreateTokenBase, nameof(c.GasFeeCreateTokenBase)),
			gasFeeCreateTokenSymbol = ParseU64(c.GasFeeCreateTokenSymbol, nameof(c.GasFeeCreateTokenSymbol)),
			gasFeeCreateTokenSeries = ParseU64(c.GasFeeCreateTokenSeries, nameof(c.GasFeeCreateTokenSeries)),
			gasFeePerByte = ParseU64(c.GasFeePerByte, nameof(c.GasFeePerByte)),
			gasFeeRegisterName = ParseU64(c.GasFeeRegisterName, nameof(c.GasFeeRegisterName)),
			gasBurnRatioMul = ParseU64(c.GasBurnRatioMul, nameof(c.GasBurnRatioMul)),
			gasBurnRatioShift = c.GasBurnRatioShift
		};
		if (result.version >= 1)
		{
			// The v2 fields are mandatory when the chain runs gas-model-v2; failing loudly here
			// beats estimating fees from silently zeroed prices.
			result.minimumGasBill = ParseU64(c.MinimumGasBill, nameof(c.MinimumGasBill));
			result.gasProducerRatioMul = ParseU64(c.GasProducerRatioMul, nameof(c.GasProducerRatioMul));
			result.gasProducerRatioShift = c.GasProducerRatioShift ?? throw MissingV2Field(nameof(c.GasProducerRatioShift));
			result.gasDappRatioMul = ParseU64(c.GasDappRatioMul, nameof(c.GasDappRatioMul));
			result.gasDappRatioShift = c.GasDappRatioShift ?? throw MissingV2Field(nameof(c.GasDappRatioShift));
			result.policyFeeCreateTokenBase = ParseU64(c.PolicyFeeCreateTokenBase, nameof(c.PolicyFeeCreateTokenBase));
			result.policyFeeCreateTokenSymbol = ParseU64(c.PolicyFeeCreateTokenSymbol, nameof(c.PolicyFeeCreateTokenSymbol));
			result.policyFeeCreateTokenSeries = ParseU64(c.PolicyFeeCreateTokenSeries, nameof(c.PolicyFeeCreateTokenSeries));
			result.policyFeeRegisterName = ParseU64(c.PolicyFeeRegisterName, nameof(c.PolicyFeeRegisterName));
			result.legacyDataEscrowPerRow = ParseU64(c.LegacyDataEscrowPerRow, nameof(c.LegacyDataEscrowPerRow));
			// The v2 byte price is a versioned consensus constant and not a config field, so the
			// calculator holds it as a constant. The node reports it in this very response. A node
			// quoting a different price runs a gas model this SDK does not implement, and every offer
			// computed from it would be wrong on the largest term of the bill. The mismatch is reported
			// here. The alternative is to under-offer every transaction until someone notices.
			if (UnitsPerBlockDataByte.HasValue && UnitsPerBlockDataByte.Value != NativeFeeEstimator.GasModelV2UnitsPerBlockDataByte)
			{
				throw new InvalidOperationException(
					$"this node prices block data at {UnitsPerBlockDataByte.Value} gas units per byte, " +
					$"this SDK implements {NativeFeeEstimator.GasModelV2UnitsPerBlockDataByte}: upgrade the SDK");
			}
		}
		return result;
	}

	private static ulong ParseU64(string? value, string fieldName)
	{
		if (string.IsNullOrEmpty(value))
			throw new FormatException($"getGasConfig field {fieldName} is missing or empty");
		return ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
	}

	private static InvalidOperationException MissingV2Field(string fieldName) =>
		new InvalidOperationException($"getGasConfig reports gas model v2 but field {fieldName} is missing");
}

/// <summary>
/// JSON shape of the on-chain GasConfig. Fields after GasBurnRatioShift exist only when the
/// config version is >= 1 (gas-model-v2) and are omitted from v1 responses.
/// </summary>
public class GasConfigDataResult
{
	[ApiDescription("Config version; >= 1 activates gas-model-v2 billing")]
	public byte Version { get; set; }
	public byte MaxNameLength { get; set; }
	public byte MaxTokenSymbolLength { get; set; }
	[ApiDescription("Right-shift applied after the fee multiplier stage")]
	public byte FeeShift { get; set; }
	public uint MaxStructureSize { get; set; }
	[ApiDescription("Multiplier from gas units to kcal-base")]
	public string? FeeMultiplier { get; set; }
	public string? GasTokenId { get; set; }
	public string? DataTokenId { get; set; }
	[ApiDescription("Minimum acceptable maxGas offer")]
	public string? MinimumGasOffer { get; set; }
	[ApiDescription("Storage escrow price per 1024-byte row quantum, in data-token atoms")]
	public string? DataEscrowPerRow { get; set; }
	[ApiDescription("Gas units charged per native transfer-class operation")]
	public string? GasFeeTransfer { get; set; }
	public string? GasFeeQuery { get; set; }
	public string? GasFeeCreateTokenBase { get; set; }
	public string? GasFeeCreateTokenSymbol { get; set; }
	public string? GasFeeCreateTokenSeries { get; set; }
	[ApiDescription("v1 price of block-carried bytes in kcal-base per byte (unused under v2)")]
	public string? GasFeePerByte { get; set; }
	public string? GasFeeRegisterName { get; set; }
	public string? GasBurnRatioMul { get; set; }
	public byte GasBurnRatioShift { get; set; }

	[ApiDescription("v2: floor applied to every settled gas bill, kcal-base")]
	public string? MinimumGasBill { get; set; }
	[ApiDescription("v2: producer fee-split ratio numerator")]
	public string? GasProducerRatioMul { get; set; }
	public byte? GasProducerRatioShift { get; set; }
	[ApiDescription("v2: dapp (gasTarget) fee-split ratio numerator")]
	public string? GasDappRatioMul { get; set; }
	public byte? GasDappRatioShift { get; set; }
	[ApiDescription("v2: token creation price, kcal-base (charged directly, no multiplier)")]
	public string? PolicyFeeCreateTokenBase { get; set; }
	[ApiDescription("v2: symbol price, halved per character after the first, kcal-base")]
	public string? PolicyFeeCreateTokenSymbol { get; set; }
	public string? PolicyFeeCreateTokenSeries { get; set; }
	[ApiDescription("v2: name registration price, shifted right by (length-1), kcal-base")]
	public string? PolicyFeeRegisterName { get; set; }
	[ApiDescription("v2: frozen pre-flip dataEscrowPerRow; pre-flip rows refund at this price")]
	public string? LegacyDataEscrowPerRow { get; set; }

	public GasConfigDataResult() { }
}
