using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class MintNonFungibleTxHelper
{
	public static TxMsg BuildTx(ulong tokenId, uint seriesId, Bytes32 to, byte[] rom, byte[]? ram, Bytes32 senderPublicKey, MintNftFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		var fees = feeOptions ?? new MintNftFeeOptions();
		var gasBase = fees.GasFeeBase;
		var multiplier = fees.FeeMultiplier;

		return new TxMsg
		{
			type = TxTypes.MintNonFungible,
			expiry = expiry ?? DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds(),
			maxGas = gasBase * multiplier,
			maxData = maxData ?? 0,
			gasFrom = senderPublicKey,
			payload = SmallString.Empty,
			msg = new TxMsgMintNonFungible
			{
				tokenId = tokenId,
				seriesId = seriesId,
				to = to,
				rom = rom,
				ram = ram ?? Array.Empty<byte>()
			}
		};
	}

	public static byte[] BuildTxAndSign(ulong tokenId, uint seriesId, Bytes32 to, byte[] rom, byte[]? ram, PhantasmaKeys signer, MintNftFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		var senderPub = new Bytes32(signer.PublicKey);
		var tx = BuildTx(tokenId, seriesId, to, rom, ram, senderPub, feeOptions, maxData, expiry);
		return TxMsgSigner.Sign(tx, signer);
	}

	public static string BuildTxAndSignHex(ulong tokenId, uint seriesId, Bytes32 to, byte[] rom, byte[]? ram, PhantasmaKeys signer, MintNftFeeOptions? feeOptions = null, ulong? maxData = null, long? expiry = null)
	{
		return BuildTxAndSign(tokenId, seriesId, to, rom, ram, signer, feeOptions, maxData, expiry).ToHex();
	}
}
