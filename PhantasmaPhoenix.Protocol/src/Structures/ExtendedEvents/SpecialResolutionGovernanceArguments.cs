using System.Collections.Generic;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

// Arguments of the governance and VM module calls a special resolution can carry. Field-for-field
// mirrors of the call payloads; numbers are strings for the reason stated on SpecialResolutionArguments.

public sealed class GasConfigArguments : SpecialResolutionArguments
{
	public string Version { get; set; } = string.Empty;

	public string MaxNameLength { get; set; } = string.Empty;

	public string MaxTokenSymbolLength { get; set; } = string.Empty;

	public string FeeShift { get; set; } = string.Empty;

	public string MaxStructureSize { get; set; } = string.Empty;

	public string FeeMultiplier { get; set; } = string.Empty;

	public string GasTokenId { get; set; } = string.Empty;

	public string DataTokenId { get; set; } = string.Empty;

	public string MinimumGasOffer { get; set; } = string.Empty;

	public string DataEscrowPerRow { get; set; } = string.Empty;

	public string GasFeeTransfer { get; set; } = string.Empty;

	public string GasFeeQuery { get; set; } = string.Empty;

	public string GasFeeCreateTokenBase { get; set; } = string.Empty;

	public string GasFeeCreateTokenSymbol { get; set; } = string.Empty;

	public string GasFeeCreateTokenSeries { get; set; } = string.Empty;

	public string GasFeePerByte { get; set; } = string.Empty;

	public string GasFeeRegisterName { get; set; } = string.Empty;

	public string GasBurnRatioMul { get; set; } = string.Empty;

	public string GasBurnRatioShift { get; set; } = string.Empty;

	// Gas-model-v2 tail: present only when the packaged config declares version >= 1, absent otherwise.
	public string? MinimumGasBill { get; set; }

	public string? GasProducerRatioMul { get; set; }

	public string? GasProducerRatioShift { get; set; }

	public string? GasDappRatioMul { get; set; }

	public string? GasDappRatioShift { get; set; }

	public string? PolicyFeeCreateTokenBase { get; set; }

	public string? PolicyFeeCreateTokenSymbol { get; set; }

	public string? PolicyFeeCreateTokenSeries { get; set; }

	public string? PolicyFeeRegisterName { get; set; }

	public string? LegacyDataEscrowPerRow { get; set; }
}

public sealed class ChainConfigArguments : SpecialResolutionArguments
{
	public string Version { get; set; } = string.Empty;

	public string Reserved1 { get; set; } = string.Empty;

	public string Reserved2 { get; set; } = string.Empty;

	public string Reserved3 { get; set; } = string.Empty;

	public string AllowedTxTypes { get; set; } = string.Empty;

	public string ExpiryWindow { get; set; } = string.Empty;

	public string BlockRateTarget { get; set; } = string.Empty;
}

/// <summary>
/// A special resolution nested inside another one. Its own calls are reported in
/// <see cref="SpecialResolutionCall.Calls"/>, not here.
/// </summary>
public sealed class NestedResolutionArguments : SpecialResolutionArguments
{
	public string ResolutionId { get; set; } = string.Empty;
}

public sealed class MetadataArguments : SpecialResolutionArguments
{
	public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public sealed class ConsensusNode
{
	public string Id { get; set; } = string.Empty;

	public string Type { get; set; } = string.Empty;
}

public sealed class NodeConfigArguments : SpecialResolutionArguments
{
	public ConsensusNode[] Nodes { get; set; } = System.Array.Empty<ConsensusNode>();
}

public sealed class RegisterNameArguments : SpecialResolutionArguments
{
	public string Address { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;
}

public sealed class AddressArguments : SpecialResolutionArguments
{
	public string Address { get; set; } = string.Empty;
}

public sealed class NameArguments : SpecialResolutionArguments
{
	public string Name { get; set; } = string.Empty;
}

public sealed class ExecuteScriptArguments : SpecialResolutionArguments
{
	public string MaxGas { get; set; } = string.Empty;

	public string GasFrom { get; set; } = string.Empty;

	public string Script { get; set; } = string.Empty;
}

public sealed class RegisterTokenContractArguments : SpecialResolutionArguments
{
	public string TokenId { get; set; } = string.Empty;

	public string Symbol { get; set; } = string.Empty;

	public string Script { get; set; } = string.Empty;

	public string Abi { get; set; } = string.Empty;

	/// <summary>Resolved token symbol; absent when the token could not be resolved at answer time.</summary>
	public string? Token { get; set; }
}

public sealed class DeployContractArguments : SpecialResolutionArguments
{
	public string From { get; set; } = string.Empty;

	public string ContractName { get; set; } = string.Empty;

	public string Script { get; set; } = string.Empty;

	public string Abi { get; set; } = string.Empty;
}

public sealed class PhantasmaVmConfigArguments : SpecialResolutionArguments
{
	public string FeatureLevel { get; set; } = string.Empty;

	public string GasConstructor { get; set; } = string.Empty;

	public string GasNexus { get; set; } = string.Empty;

	public string GasOrganization { get; set; } = string.Empty;

	public string GasAccount { get; set; } = string.Empty;

	public string GasLeaderboard { get; set; } = string.Empty;

	public string GasStandard { get; set; } = string.Empty;

	public string GasOracle { get; set; } = string.Empty;

	public string FuelPerContractDeploy { get; set; } = string.Empty;
}
