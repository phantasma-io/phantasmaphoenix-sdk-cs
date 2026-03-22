using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static class MintPhantasmaNonFungibleTxHelper
{
	public static TxMsg BuildTx(
		ulong tokenId,
		BigInteger phantasmaSeriesId,
		Bytes32 senderPublicKey,
		Bytes32 receiverPublicKey,
		byte[] publicRom,
		byte[]? ram,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		return BuildTx(
			tokenId,
			senderPublicKey,
			receiverPublicKey,
			new[]
			{
				new PhantasmaNftMintInfo
				{
					phantasmaSeriesId = new IntX(phantasmaSeriesId),
					rom = publicRom,
					ram = ram ?? Array.Empty<byte>()
				}
			},
			feeOptions,
			maxData,
			expiry);
	}

	public static TxMsg BuildTx(
		ulong tokenId,
		Bytes32 senderPublicKey,
		Bytes32 receiverPublicKey,
		PhantasmaNftMintInfo[] tokens,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		if (tokens == null)
		{
			throw new ArgumentNullException(nameof(tokens));
		}

		// Match the raw mint fee calculation. This helper only packages the Token.Call ABI surface.
		var fees = feeOptions ?? new MintNftFeeOptions();
		ulong maxGas = fees.CalculateMaxGas(tokens);

		return new TxMsg
		{
			type = TxTypes.Call,
			expiry = expiry ?? DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeMilliseconds(),
			maxGas = maxGas,
			maxData = maxData ?? 0,
			gasFrom = senderPublicKey,
			payload = SmallString.Empty,
			msg = new TxMsgCall
			{
				moduleId = (uint)ModuleId.Token,
				methodId = (uint)TokenContract_Methods.MintPhantasmaNonFungible,
				args = CarbonBlob.Serialize(new MintPhantasmaNonFungibleArgs
				{
					tokenId = tokenId,
					address = receiverPublicKey,
					tokens = tokens
				})
			}
		};
	}

	public static byte[] BuildTxAndSign(
		ulong tokenId,
		BigInteger phantasmaSeriesId,
		PhantasmaKeys signer,
		Bytes32 receiverPublicKey,
		byte[] publicRom,
		byte[]? ram,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		var senderPub = new Bytes32(signer.PublicKey);
		var tx = BuildTx(tokenId, phantasmaSeriesId, senderPub, receiverPublicKey, publicRom, ram, feeOptions, maxData, expiry);
		return TxMsgSigner.Sign(tx, signer);
	}

	public static byte[] BuildTxAndSign(
		ulong tokenId,
		PhantasmaNftMintInfo[] tokens,
		PhantasmaKeys signer,
		Bytes32 receiverPublicKey,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		var senderPub = new Bytes32(signer.PublicKey);
		var tx = BuildTx(tokenId, senderPub, receiverPublicKey, tokens, feeOptions, maxData, expiry);
		return TxMsgSigner.Sign(tx, signer);
	}

	public static string BuildTxAndSignHex(
		ulong tokenId,
		BigInteger phantasmaSeriesId,
		PhantasmaKeys signer,
		Bytes32 receiverPublicKey,
		byte[] publicRom,
		byte[]? ram,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		return BuildTxAndSign(tokenId, phantasmaSeriesId, signer, receiverPublicKey, publicRom, ram, feeOptions, maxData, expiry).ToHex();
	}

	public static string BuildTxAndSignHex(
		ulong tokenId,
		PhantasmaNftMintInfo[] tokens,
		PhantasmaKeys signer,
		Bytes32 receiverPublicKey,
		MintNftFeeOptions? feeOptions = null,
		ulong? maxData = null,
		long? expiry = null)
	{
		return BuildTxAndSign(tokenId, tokens, signer, receiverPublicKey, feeOptions, maxData, expiry).ToHex();
	}

	public static PhantasmaNftMintResult[] ParseResult(string resultHex)
	{
		var bytes = Base16.Decode(resultHex) ?? Array.Empty<byte>();
		using var s = new MemoryStream(bytes);
		using var r = new BinaryReader(s);
		r.ReadArray(out PhantasmaNftMintResult[] result);
		return result;
	}
}
