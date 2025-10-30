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
		var fieldsJson = "{\"name\":\"My test token!\",\"icon\":\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'%3E%3Cpath fill='%23F44336' d='M7 4h5a5 5 0 010 10H9v6H7zM9 6v6h3a3 3 0 000-6z'/%3E%3C/svg%3E\",\"url\":\"http://example.com\",\"description\":\"My test token description\"}";
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
			TokenMetadataBuilder.BuildAndSerialize(fields));

		var feeOptions = new CreateTokenFeeOptions(
			gasFeeBase,
			gasFeeCreateTokenBase,
			gasFeeCreateTokenSymbol,
			feeMultiplier
		);

		var tx = CreateTokenTxHelper.BuildTx(tokenInfo,
			txSenderPubKey,
			feeOptions,
			maxData,
			1759711416000);

		return CarbonBlob.Serialize(tx).ToHex();
	}
}
