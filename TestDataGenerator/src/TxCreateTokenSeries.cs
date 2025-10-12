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
	public static string TxCreateTokenSeries()
	{
		var wif = "KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d";
		ulong tokenId = ulong.MaxValue;
		var fieldsJson = "{\"name\": \"My test token!\", \"url\": \"http://example.com\"}";
		ulong maxData = 100000000;
		ulong gasFeeBase = 10000;
		ulong gasFeeCreateTokenSeries = 2500000000;

		var txSender = PhantasmaKeys.FromWIF(wif);

		Dictionary<string, string>? fields =
			JsonConvert.DeserializeObject<Dictionary<string, string>>(
				fieldsJson
			);
		if(fields == null)
        {
			throw new("Could not deserialize fields");
        }

		var newPhantasmaSeriesId = (BigInteger.One << 256) - 1; // Phantasma series ID
		byte[] sharedRom = [];// todo

		// Write out the variables that are expected for a new series (encoded with respect to the seriesMetadataSchema used when creating the token)
		var tokenSchemas = PrepareTokenSchemas();
		using MemoryStream metadataBuffer = new();
		using BinaryWriter wMetadata = new(metadataBuffer);
		new VmDynamicStruct
		{
			fields = [
				new VmNamedDynamicVariable{ name = StandardMeta.id, value = new VmDynamicVariable(newPhantasmaSeriesId) },
				new VmNamedDynamicVariable{ name = new SmallString("mode"), value = new VmDynamicVariable((byte)(sharedRom.Length == 0 ? 0 : 1)) },
				new VmNamedDynamicVariable{ name = new SmallString("rom"), value = new VmDynamicVariable(sharedRom) },
			]
		}.Write(tokenSchemas.seriesMetadata, wMetadata);

		// CreateTokenSeries expects (int64, SeriesInfo) args
		using MemoryStream argsBuffer = new();
		using BinaryWriter wArgs = new(argsBuffer);
		wArgs.Write8(tokenId);
		wArgs.Write(new SeriesInfo
		{
			maxMint = 0, // limit on minting, or 0=no limit
			maxSupply = 0, // limit on how many can exist at once
			owner = new Bytes32(txSender.PublicKey),
			metadata = metadataBuffer.ToArray(), // VmDynamicStruct encoded with TokenInfo.tokenSchemas.seriesMetadata
			rom = new VmStructSchema { fields = [], flags = VmStructSchema.Flags.None },
			ram = new VmStructSchema { fields = [], flags = VmStructSchema.Flags.None },
		});

		TxMsg tx = new TxMsg
		{
			type = TxTypes.Call, // Generic transaction type - Call a single function
			expiry = 1759711416000,
			maxGas = (gasFeeBase + gasFeeCreateTokenSeries) * 10000,
			maxData = maxData,
			gasFrom = new Bytes32(txSender.PublicKey),
			payload = SmallString.Empty,
			msg = new TxMsgCall
			{
				moduleId = (uint)ModuleId.Token, // Call a method in the "token" module
				methodId = (uint)TokenContract_Methods.CreateTokenSeries,// Call the CreateTokenSeries method
				args = argsBuffer.ToArray() // CreateTokenSeries expects (int64, SeriesInfo) args
			}
		};

		return CarbonBlob.Serialize(tx).ToHex();
	}
}
