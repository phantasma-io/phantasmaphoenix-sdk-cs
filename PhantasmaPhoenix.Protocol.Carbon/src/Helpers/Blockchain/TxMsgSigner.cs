using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public static class TxMsgSigner
{
	/// <summary>
	/// Signs a TxMsg with the given PhantasmaKeys and returns serialized SignedTxMsg as byte[].
	/// </summary>
	public static byte[] Sign(TxMsg msg, PhantasmaKeys keys)
	{
		var sig = new Bytes64(Ed25519.Sign(CarbonBlob.Serialize(msg), keys.PrivateKey));
		var witness = new Witness
		{
			address = new Bytes32(keys.PublicKey),
			signature = sig
		};
		var signed = new SignedTxMsg
		{
			msg = msg,
			witnesses = new Witness[] { witness }
		};
		return CarbonBlob.Serialize(signed);
	}
}
