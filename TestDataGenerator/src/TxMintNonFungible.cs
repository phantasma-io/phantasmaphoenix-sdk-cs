using System.Numerics;
using Newtonsoft.Json;
using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public static partial class TxGenerators
{
	public static string TxMintNonFungible()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		ulong carbonTokenId = ulong.MaxValue;
		uint carbonSeriesId = uint.MaxValue;
		var fieldsJson = "{\"name\": \"My test token!\", \"url\": \"http://example.com\"}";
		var romHex = "";
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;

		var txSender = PhantasmaKeys.FromWIF(wif);

		Dictionary<string, string>? fields =
			JsonConvert.DeserializeObject<Dictionary<string, string>>(
				fieldsJson
			);
		if(fields == null)
        {
			throw new("Could not deserialize fields");
        }

		BigInteger phantasmaId = (BigInteger.One << 256) - 1; // Arbitrary phantasma ID
		byte[] phantasmaRomData = [0x01, 0x42]; // todo - arbitrary / TOMB data

		if (!string.IsNullOrWhiteSpace(romHex))
		{
			phantasmaRomData = Convert.FromHexString(romHex);
		}

		// Write out the variables that are expected for a new NFT instance (encoded with respect to the rom schema used when creating the token)
		var tokenSchemas = PrepareTokenSchemas();
		using MemoryStream romBuffer = new();
		using BinaryWriter wRom = new(romBuffer);
		new VmDynamicStruct
		{
			fields = [
				new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(phantasmaId) },
				new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(phantasmaRomData) },
			]
		}.Write(tokenSchemas.rom, wRom);

		TxMsg tx = new TxMsg
		{
			type = TxTypes.MintNonFungible, // Specialized minting TX
			expiry = 1759711416000,
			maxGas = gasFeeBase * 1000,
			maxData = maxData,
			gasFrom = new Bytes32(txSender.PublicKey),
			payload = SmallString.Empty,
			msg = new TxMsgMintNonFungible
			{
				tokenId = carbonTokenId,
				seriesId = carbonSeriesId,
				to = new Bytes32(txSender.PublicKey),
				rom = romBuffer.ToArray(),
				ram = []
			}
		};

		return CarbonBlob.Serialize(tx).ToHex();
	}
}
