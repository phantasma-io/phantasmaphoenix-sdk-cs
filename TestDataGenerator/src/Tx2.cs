using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public static partial class TxGenerators
{
	public static string Tx2Gen()
	{
		var txSender = PhantasmaKeys.FromWIF("KwPpBSByydVKqStGHAnZzQofCqhDmD2bfRgc9BmZqM3ZmsdWJw4d");
		var txReceiver = PhantasmaKeys.FromWIF("KwVG94yjfVg1YKFyRxAGtug93wdRbmLnqqrFV6Yd2CiA9KZDAp4H");

		var tx = new TxMsg
		{
			type = TxTypes.TransferFungible,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = new Bytes32(txSender.PublicKey),
			payload = new SmallString("test-payload"),
			msg = new TxMsgTransferFungible
			{
				to = new Bytes32(txReceiver.PublicKey),
				tokenId = 1,
				amount = 100000000
			}
		};

		var signedTxMsg = new SignedTxMsg
		{
			msg = tx,
			witnesses = new Witness[] {
				new Witness
				{
					address = new Bytes32(txSender.PublicKey),
					signature = new Bytes64(Ed25519.Sign(CarbonBlob.Serialize(tx), txSender.PrivateKey))
				}
			}
		};

		return CarbonBlob.Serialize(signedTxMsg).ToHex();
	}
}
