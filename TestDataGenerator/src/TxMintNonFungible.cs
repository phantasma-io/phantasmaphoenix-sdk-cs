using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules.Builders;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.TxHelpers;

public static partial class TxGenerators
{
	public static string TxMintNonFungible()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		ulong carbonTokenId = ulong.MaxValue;
		uint carbonSeriesId = uint.MaxValue;
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong feeMultiplier = 1000;

		var txSender = PhantasmaKeys.FromWIF(wif);

		BigInteger phantasmaId = (BigInteger.One << 256) - 1; // Arbitrary phantasma ID
		byte[] phantasmaRomData = [0x01, 0x42]; // todo - arbitrary / TOMB data

		var rom = NftRomBuilder.BuildAndSerialize(phantasmaId, phantasmaRomData, null);

		var feeOptions = new MintNftFeeOptions(
			gasFeeBase,
			feeMultiplier
		);

		var tx = MintNonFungibleTxHelper.BuildTx(
			carbonTokenId,
			carbonSeriesId,
			new Bytes32(txSender.PublicKey),
			rom,
			Array.Empty<byte>(),
			new Bytes32(txSender.PublicKey),
			feeOptions,
			maxData,
			1759711416000
		);
		return CarbonBlob.Serialize(tx).ToHex();
	}
}
