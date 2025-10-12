using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public static partial class TxGenerators
{
	public static string Tx1Gen()
	{
		var tx = new TxMsg
		{
			type = TxTypes.TransferFungible,
			expiry = 1759711416000,
			maxGas = 10000000,
			maxData = 1000,
			gasFrom = new Bytes32(),
			payload = new SmallString("test-payload"),
			msg = new TxMsgTransferFungible
			{
				to = new Bytes32(),
				tokenId = 1,
				amount = 100000000
			}
		};

		return CarbonBlob.Serialize(tx).ToHex();
	}
}
