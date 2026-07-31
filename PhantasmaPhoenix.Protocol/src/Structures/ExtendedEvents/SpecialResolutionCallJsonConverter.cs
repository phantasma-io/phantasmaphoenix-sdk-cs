using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

/// <summary>
/// Reads a special-resolution call and gives its arguments the type that belongs to the called method.
/// </summary>
/// <remarks>
/// The discriminator is the call itself - its module and method names - so nothing extra has to travel
/// on the wire for this to work. Dispatching on the names rather than on the numeric ids keeps the
/// mapping readable and independent of the Carbon method enums, which live in another package.
///
/// Write side is left to the default contract: the answering node produces these objects and its own
/// serializer settings decide the field names.
/// </remarks>
internal sealed class SpecialResolutionCallJsonConverter : JsonConverter<SpecialResolutionCall>
{
	public override bool CanWrite => false;

	private const string GovernanceModule = "governance";
	private const string TokenModule = "token";
	private const string PhantasmaVmModule = "phantasma_vm";

	// Method name -> argument type, per module. A method missing here is not an error: the node answers
	// with the raw argument buffer for anything it cannot decode, and that is what RawArguments carries.
	private static readonly Dictionary<string, Dictionary<string, Type>> ArgumentTypes = new(StringComparer.Ordinal)
	{
		[GovernanceModule] = new Dictionary<string, Type>(StringComparer.Ordinal)
		{
			["SetGasConfig"] = typeof(GasConfigArguments),
			["SetChainConfig"] = typeof(ChainConfigArguments),
			["SpecialResolution"] = typeof(NestedResolutionArguments),
			["SetMetadata"] = typeof(MetadataArguments),
			["SetNodeConfig"] = typeof(NodeConfigArguments),
			["RegisterName"] = typeof(RegisterNameArguments),
			["LookupName"] = typeof(AddressArguments),
			["LookupAddress"] = typeof(NameArguments)
		},
		[PhantasmaVmModule] = new Dictionary<string, Type>(StringComparer.Ordinal)
		{
			["ExecuteScript"] = typeof(ExecuteScriptArguments),
			["RegisterTokenContract"] = typeof(RegisterTokenContractArguments),
			["DeployContract"] = typeof(DeployContractArguments),
			["IsContractDeployed"] = typeof(NameArguments),
			["SetConfig"] = typeof(PhantasmaVmConfigArguments),
			["ImportContracts"] = typeof(ImportContractsArguments),
			["RepairSeries"] = typeof(RepairSeriesArguments),
			["RepairToken"] = typeof(RepairTokenArguments)
		},
		[TokenModule] = new Dictionary<string, Type>(StringComparer.Ordinal)
		{
			["TransferFungible"] = typeof(TransferFungibleArguments),
			["TransferNonFungible"] = typeof(TransferNonFungibleArguments),
			["CreateToken"] = typeof(CreateTokenArguments),
			["MintFungible"] = typeof(MintFungibleArguments),
			["BurnFungible"] = typeof(BurnFungibleArguments),
			["GetBalance"] = typeof(BalanceArguments),
			["CreateTokenSeries"] = typeof(TokenSeriesArguments),
			["DeleteTokenSeries"] = typeof(TokenSeriesReferenceArguments),
			["MintNonFungible"] = typeof(MintNonFungibleArguments),
			["BurnNonFungible"] = typeof(BurnNonFungibleArguments),
			["GetNonFungibleInfo"] = typeof(NonFungibleInfoArguments),
			["GetNonFungibleInfoByRomId"] = typeof(NonFungibleInfoByRomIdArguments),
			["GetSeriesInfo"] = typeof(TokenSeriesReferenceArguments),
			["GetSeriesInfoByMetaId"] = typeof(SeriesInfoByMetaIdArguments),
			["GetTokenInfo"] = typeof(TokenReferenceArguments),
			["GetTokenInfoBySymbol"] = typeof(SymbolArguments),
			["GetTokenSupply"] = typeof(TokenReferenceArguments),
			["GetSeriesSupply"] = typeof(TokenSeriesReferenceArguments),
			["GetTokenIdBySymbol"] = typeof(SymbolArguments),
			["GetBalances"] = typeof(AddressArguments),
			["CreateMintedTokenSeries"] = typeof(CreateMintedTokenSeriesArguments),
			["ApplyInflation"] = typeof(TokenReferenceArguments),
			["UpdateTokenMetadata"] = typeof(UpdateTokenMetadataArguments),
			["GetNextTokenInflation"] = typeof(TokenReferenceArguments),
			["SetTokensConfig"] = typeof(TokensConfigArguments),
			["UpdateSeriesMetadata"] = typeof(UpdateSeriesMetadataArguments),
			["MintPhantasmaNonFungible"] = typeof(MintPhantasmaNonFungibleArguments)
		}
	};

	public override SpecialResolutionCall ReadJson(
		JsonReader reader,
		Type objectType,
		SpecialResolutionCall existingValue,
		bool hasExistingValue,
		JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return default;

		var obj = JObject.Load(reader);

		var moduleId = obj.Value<uint?>("moduleId") ?? 0;
		var module = obj.Value<string>("module") ?? string.Empty;
		var methodId = obj.Value<uint?>("methodId") ?? 0;
		var method = obj.Value<string>("method") ?? string.Empty;

		var calls = obj["calls"] is JArray nested
			? nested.ToObject<SpecialResolutionCall[]>(serializer)
			: null;

		return new SpecialResolutionCall(
			moduleId,
			module,
			methodId,
			method,
			ReadArguments(obj["arguments"], module, method, serializer),
			calls);
	}

	private static SpecialResolutionArguments? ReadArguments(
		JToken? arguments,
		string module,
		string method,
		JsonSerializer serializer)
	{
		if (arguments is not JObject argumentObject)
			return null;

		// The undecoded case is recognised by its content, not by the method name: a method this build
		// knows can still arrive as a raw dump from an older node, and reading it as the typed shape
		// would silently produce an object with every field empty.
		if (argumentObject["rawArgs"] != null)
			return argumentObject.ToObject<RawArguments>(serializer);

		if (ArgumentTypes.TryGetValue(module, out var methods)
			&& methods.TryGetValue(method, out var argumentType))
		{
			return (SpecialResolutionArguments?)argumentObject.ToObject(argumentType, serializer);
		}

		return null;
	}

	public override void WriteJson(JsonWriter writer, SpecialResolutionCall value, JsonSerializer serializer)
	{
		throw new NotSupportedException($"{nameof(SpecialResolutionCallJsonConverter)} is read-only.");
	}
}
