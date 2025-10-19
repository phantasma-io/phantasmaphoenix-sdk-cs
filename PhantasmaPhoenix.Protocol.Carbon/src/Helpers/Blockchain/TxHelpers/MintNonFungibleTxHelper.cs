using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class MintNonFungibleTxHelper
{
	public static TxMsg BuildTx(
		ulong tokenId,
		uint seriesId,
		Bytes32 senderPublicKey,
		Bytes32 receiverPublicKey,
		byte[] rom,
		byte[]? ram,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
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
				to = receiverPublicKey,
				rom = rom,
				ram = ram ?? Array.Empty<byte>()
			}
		};
	}

	public static byte[] BuildTxAndSign(
		ulong tokenId,
		uint seriesId,
		PhantasmaKeys signer,
		Bytes32 receiverPublicKey,
		byte[] rom,
		byte[]? ram,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		var senderPub = new Bytes32(signer.PublicKey);
		var tx = BuildTx(tokenId, seriesId, senderPub, receiverPublicKey, rom, ram, feeOptions, maxData, expiry);
		return TxMsgSigner.Sign(tx, signer);
	}

	public static string BuildTxAndSignHex(
		ulong tokenId,
		uint seriesId,
		PhantasmaKeys signer,
		Bytes32 receiverPublicKey,
		byte[] rom,
		byte[]? ram,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		return BuildTxAndSign(tokenId, seriesId, signer, receiverPublicKey, rom, ram, feeOptions, maxData, expiry).ToHex();
	}
}
