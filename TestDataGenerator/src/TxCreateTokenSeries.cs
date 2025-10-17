using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;

public static partial class TxGenerators
{
	public static string TxCreateTokenSeries()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		ulong tokenId = ulong.MaxValue;
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong gasFeeCreateTokenSeries = 2500000000;
		ulong feeMultiplier = 10000;

		var txSender = PhantasmaKeys.FromWIF(wif);
		var txSenderPubKey = new Bytes32(txSender.PublicKey);

		var newPhantasmaSeriesId = (BigInteger.One << 256) - 1; // Phantasma series ID

		var seriesInfo = SeriesInfoBuilder.Build(newPhantasmaSeriesId,
			0,
			0,
			txSenderPubKey);

		var feeOptions = new CreateSeriesFeeOptions(
			gasFeeBase,
			gasFeeCreateTokenSeries,
			feeMultiplier
		);

		var tx = CreateTokenSeriesTxHelper.BuildTx(tokenId,
			seriesInfo,
			txSenderPubKey,
			feeOptions,
			maxData,
			1759711416000);

		return CarbonBlob.Serialize(tx).ToHex();
	}
}
