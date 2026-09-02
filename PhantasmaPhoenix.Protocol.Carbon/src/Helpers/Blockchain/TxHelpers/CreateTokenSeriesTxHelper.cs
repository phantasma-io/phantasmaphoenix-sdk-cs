using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class CreateTokenSeriesTxHelper
{
	/// <summary>Builds the Token.CreateTokenSeries call. Fees are planned from the message afterwards.</summary>
	public static TxMsg BuildTx(ulong tokenId, SeriesInfo seriesInfo, Bytes32 creatorPublicKey, TxLimits? limits = null)
	{
		return TxLimits.Apply(new TxMsg
		{
			type = TxTypes.Call,
			gasFrom = creatorPublicKey,
			payload = SmallString.Empty,
			msg = new TxMsgCall
			{
				moduleId = (uint)ModuleId.Token,
				methodId = (uint)TokenContract_Methods.CreateTokenSeries,
				args = CarbonBlob.Serialize(new CreateTokenSeriesArgs
				{
					tokenId = tokenId,
					info = seriesInfo
				})
			}
		}, limits);
	}

	/// <summary>Builds, plans against <paramref name="config"/> and signs with in-memory keys, returning the envelope bytes.</summary>
	public static byte[] BuildTxAndSign(ulong tokenId, SeriesInfo seriesInfo, PhantasmaKeys signer, in GasConfig config, PlanAndSignOptions? options = null)
	{
		var tx = BuildTx(tokenId, seriesInfo, new Bytes32(signer.PublicKey), options?.Limits);
		return PlanAndSign.WithKeys(tx, new IKeyPair[] { signer }, config, options);
	}

	public static string BuildTxAndSignHex(ulong tokenId, SeriesInfo seriesInfo, PhantasmaKeys signer, in GasConfig config, PlanAndSignOptions? options = null)
	{
		return BuildTxAndSign(tokenId, seriesInfo, signer, config, options).ToHex();
	}

	/// <summary>The series id the creation returned: Token.CreateTokenSeries answers with a u32.</summary>
	public static uint ParseResult(string resultHex)
	{
		uint carbonSeriesId;
		using (var s = new MemoryStream(Base16.Decode(resultHex) ?? Array.Empty<byte>()))
		using (var r = new BinaryReader(s))
		{
			BinaryStreamExt.Read4(r, out carbonSeriesId);
		}

		return carbonSeriesId;
	}
}
