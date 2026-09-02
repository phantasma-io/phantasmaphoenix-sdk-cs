using Newtonsoft.Json;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static partial class TxGenerators
{
	public static string TxCreateToken()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		var symbol = "MYNFT";
		var fieldsJson = "{\"name\":\"My test token!\",\"icon\":\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==\",\"url\":\"http://example.com\",\"description\":\"My test token description\"}";
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong gasFeeCreateTokenBase = 10000000000;
		ulong gasFeeCreateTokenSymbol = 10000000000;
		ulong feeMultiplier = 10000;

		var txSender = PhantasmaKeys.FromWIF(wif);
		var txSenderPubKey = new Bytes32(txSender.PublicKey);

		Dictionary<string, string>? fields =
			JsonConvert.DeserializeObject<Dictionary<string, string>>(
				fieldsJson
			);
		if (fields == null)
		{
			throw new("Could not deserialize fields");
		}

		var tokenInfo = TokenInfoBuilder.Build(symbol,
			new IntX(0),
			true,
			0,
			txSenderPubKey,
			TokenMetadataBuilder.BuildAndSerialize(fields),
			TokenSchemasBuilder.BuildAndSerialize(null));

		// The vector pins the gas-model-v1 offer the SDK used to compute from fee constants:
		// (base + token price + symbol price halved per character after the first) x multiplier.
		var symbolShift = symbol.Length > 0 ? symbol.Length - 1 : 0;
		var maxGas = (gasFeeBase + gasFeeCreateTokenBase + (gasFeeCreateTokenSymbol >> symbolShift)) * feeMultiplier;

		var tx = CreateTokenTxHelper.BuildTx(tokenInfo,
			txSenderPubKey,
			new TxLimits { MaxGas = maxGas, MaxData = maxData, Expiry = 1759711416000 });

		return CarbonBlob.Serialize(tx).ToHex();
	}
}
