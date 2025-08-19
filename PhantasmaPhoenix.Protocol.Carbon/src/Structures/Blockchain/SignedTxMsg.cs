using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct SignedTxMsg : ICarbonBlob
{
	public TxMsg msg;
	public Witness[] witnesses;
	public void Write(BinaryWriter w)
	{
		w.Write(msg);
		switch (msg.type)
		{
			case TxTypes.TransferFungible:
			case TxTypes.TransferNonFungible_Single:
			case TxTypes.TransferNonFungible_Multi:
			case TxTypes.MintFungible:
			case TxTypes.BurnFungible:
			case TxTypes.MintNonFungible:
			case TxTypes.BurnNonFungible:
				Throw.Assert(witnesses.Length == 1 && witnesses[0].address.Equals(msg.gasFrom));
				w.Write<Bytes64>(witnesses[0].signature);
				break;

			case TxTypes.TransferFungible_GasPayer:
			case TxTypes.TransferNonFungible_Single_GasPayer:
			case TxTypes.TransferNonFungible_Multi_GasPayer:
			case TxTypes.BurnFungible_GasPayer:
			case TxTypes.BurnNonFungible_GasPayer:
				Bytes32 from;
				switch (msg.type)
				{
					default: Throw.Assert(false); return;
					case TxTypes.TransferFungible_GasPayer: from = ((TxMsgTransferFungible_GasPayer)msg.msg).from; break;
					case TxTypes.TransferNonFungible_Single_GasPayer: from = ((TxMsgTransferNonFungible_Single_GasPayer)msg.msg).from; break;
					case TxTypes.TransferNonFungible_Multi_GasPayer: from = ((TxMsgTransferNonFungible_Multi_GasPayer)msg.msg).from; break;
					case TxTypes.BurnFungible_GasPayer: from = ((TxMsgBurnFungible_GasPayer)msg.msg).from; break;
					case TxTypes.BurnNonFungible_GasPayer: from = ((TxMsgBurnNonFungible_GasPayer)msg.msg).from; break;
				}
				Throw.Assert(witnesses.Length == 2 &&
							  witnesses[0].address.Equals(msg.gasFrom) &&
							  witnesses[1].address.Equals(from));
				w.Write<Bytes64>(witnesses[0].signature);
				w.Write<Bytes64>(witnesses[1].signature);
				break;

			case TxTypes.Call:
			case TxTypes.Call_Multi:
			case TxTypes.Trade:
			case TxTypes.Phantasma:
				w.WriteArray(witnesses);
				break;

			case TxTypes.Phantasma_Raw:
				Throw.Assert(witnesses.Length == 0);
				break;

			default:
				Throw.If(true, "Unsupported transaction type");
				break;
		}
	}
	public void Read(BinaryReader r)
	{
		r.Read(out msg);
		switch (msg.type)
		{
			case TxTypes.TransferFungible:
			case TxTypes.TransferNonFungible_Single:
			case TxTypes.TransferNonFungible_Multi:
			case TxTypes.MintFungible:
			case TxTypes.BurnFungible:
			case TxTypes.MintNonFungible:
			case TxTypes.BurnNonFungible:
				witnesses = new Witness[]{
					new Witness { address = msg.gasFrom, signature = r.Read<Bytes64>() }
				};
				break;

			case TxTypes.TransferFungible_GasPayer:
			case TxTypes.TransferNonFungible_Single_GasPayer:
			case TxTypes.TransferNonFungible_Multi_GasPayer:
			case TxTypes.BurnFungible_GasPayer:
			case TxTypes.BurnNonFungible_GasPayer:
				Bytes32 from;
				switch (msg.type)
				{
					default: Throw.Assert(false); return;
					case TxTypes.TransferFungible_GasPayer: from = ((TxMsgTransferFungible_GasPayer)msg.msg).from; break;
					case TxTypes.TransferNonFungible_Single_GasPayer: from = ((TxMsgTransferNonFungible_Single_GasPayer)msg.msg).from; break;
					case TxTypes.TransferNonFungible_Multi_GasPayer: from = ((TxMsgTransferNonFungible_Multi_GasPayer)msg.msg).from; break;
					case TxTypes.BurnFungible_GasPayer: from = ((TxMsgBurnFungible_GasPayer)msg.msg).from; break;
					case TxTypes.BurnNonFungible_GasPayer: from = ((TxMsgBurnNonFungible_GasPayer)msg.msg).from; break;
				}
				witnesses = new Witness[]{
					new Witness { address = msg.gasFrom, signature = r.Read<Bytes64>() },
					new Witness { address =        from, signature = r.Read<Bytes64>() },
				};
				break;

			case TxTypes.Call:
			case TxTypes.Call_Multi:
			case TxTypes.Trade:
			case TxTypes.Phantasma:
				witnesses = r.ReadArray<Witness>();
				break;

			case TxTypes.Phantasma_Raw:
				witnesses = Array.Empty<Witness>();
				break;

			default:
				Throw.If(true, "Unsupported transaction type");
				break;
		}
	}
}
