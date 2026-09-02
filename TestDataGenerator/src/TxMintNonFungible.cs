using System.Numerics;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
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

		var tokenSchemas = TokenSchemasBuilder.PrepareStandardTokenSchemas();
		var metadataFields = new[]
		{
			new MetadataField("name", "My NFT #1"),
			new MetadataField("description", "This is my first NFT!"),
			new MetadataField("imageURL", "images-assets.nasa.gov/image/PIA13227/PIA13227~orig.jpg"),
			new MetadataField("infoURL", "https://images.nasa.gov/details/PIA13227"),
			new MetadataField("royalties", 10000000),
			new MetadataField("rom", phantasmaRomData)
		};

		var rom = NftRomBuilder.BuildAndSerialize(tokenSchemas.rom, phantasmaId, metadataFields);

		// The SDK ships no builder for the native NFT mint (the chain's token config disables the
		// operation), so the vector is the raw wire message with the gas-model-v1 offer it always had.
		var tx = new TxMsg
		{
			type = TxTypes.MintNonFungible,
			expiry = 1759711416000,
			maxGas = gasFeeBase * feeMultiplier,
			maxData = maxData,
			gasFrom = new Bytes32(txSender.PublicKey),
			payload = SmallString.Empty,
			msg = new TxMsgMintNonFungible
			{
				tokenId = carbonTokenId,
				seriesId = carbonSeriesId,
				to = new Bytes32(txSender.PublicKey),
				rom = rom,
				ram = Array.Empty<byte>()
			}
		};
		return CarbonBlob.Serialize(tx).ToHex();
	}
}
