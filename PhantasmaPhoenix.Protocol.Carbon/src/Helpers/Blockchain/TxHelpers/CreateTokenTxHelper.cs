using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class CreateTokenTxHelper
{
	public static TxMsg BuildTx(TokenInfo tokenInfo, Bytes32 creatorPublicKey, CreateTokenFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		var fees = feeOptions ?? new CreateTokenFeeOptions();
		ulong maxGas = fees.CalculateMaxGas(tokenInfo.symbol);

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
				methodId = (uint)TokenContract_Methods.CreateToken,
				args = CarbonBlob.Serialize(tokenInfo)
			}
		};
	}

	public static byte[] BuildTxAndSign(TokenInfo tokenInfo, PhantasmaKeys signer, CreateTokenFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		var tx = BuildTx(tokenInfo, new Bytes32(signer.PublicKey), feeOptions, maxData, expiry);
		return TxMsgSigner.Sign(tx, signer);
	}

	public static string BuildTxAndSignHex(TokenInfo tokenInfo, PhantasmaKeys signer, CreateTokenFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		return BuildTxAndSign(tokenInfo, signer, feeOptions, maxData, expiry).ToHex();
	}

	public static ulong ParseResult(string resultHex)
	{
		ulong carbonTokenId;
		using (var s = new MemoryStream(Base16.Decode(resultHex)))
		using (var r = new BinaryReader(s))
		{
			BinaryStreamExt.Read4(r, out carbonTokenId);
		}

		return carbonTokenId;
	}
}
