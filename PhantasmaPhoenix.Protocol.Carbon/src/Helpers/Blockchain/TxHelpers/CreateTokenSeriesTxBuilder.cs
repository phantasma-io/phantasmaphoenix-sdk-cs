using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class CreateTokenSeriesTxHelper
{
	public static TxMsg BuildTx(ulong tokenId, SeriesInfo seriesInfo, Bytes32 creatorPublicKey, CreateSeriesFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		var fees = feeOptions ?? new CreateSeriesFeeOptions();
		ulong maxGas = fees.CalculateMaxGas();

		using var argsStream = new MemoryStream();
		using var w = new BinaryWriter(argsStream);
		w.Write8(tokenId);
		w.Write(CarbonBlob.Serialize(seriesInfo));

		return new TxMsg
		{
			type = TxTypes.Call,
			expiry = expiry ?? DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds(),
			maxGas = maxGas,
			maxData = maxData ?? 0,
			gasFrom = creatorPublicKey,
			payload = SmallString.Empty,
			msg = new TxMsgCall
			{
				moduleId = (uint)ModuleId.Token,
				methodId = (uint)TokenContract_Methods.CreateTokenSeries,
				args = argsStream.ToArray()
			}
		};
	}

	public static byte[] BuildTxAndSign(ulong tokenId, SeriesInfo seriesInfo, PhantasmaKeys signer, CreateSeriesFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		var tx = BuildTx(tokenId, seriesInfo, new Bytes32(signer.PublicKey), feeOptions, maxData, expiry);
		return TxMsgSigner.Sign(tx, signer);
	}

	public static string BuildTxAndSignHex(ulong tokenId, SeriesInfo seriesInfo, PhantasmaKeys signer, CreateSeriesFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		return BuildTxAndSign(tokenId, seriesInfo, signer, feeOptions, maxData, expiry).ToHex();
	}

	public static uint ParseResult(string resultHex)
	{
		uint carbonSeriesId;
		using (var s = new MemoryStream(Base16.Decode(resultHex)))
		using (var r = new BinaryReader(s))
		{
			BinaryStreamExt.Read4(r, out carbonSeriesId);
		}

		return carbonSeriesId;
	}
}
