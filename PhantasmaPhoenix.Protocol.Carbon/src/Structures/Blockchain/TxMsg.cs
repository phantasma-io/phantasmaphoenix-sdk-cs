using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsg : ICarbonBlob
{
	public TxTypes type;
	public Int64 expiry;
	public UInt64 maxGas;
	public UInt64 maxData;
	public Bytes32 gasFrom;
	public SmallString payload;
	public object msg;

	static void WriteDataByType(BinaryWriter w, TxTypes type, object msg)
	{
		switch (type)
		{
			case TxTypes.Call: w.Write((TxMsgCall)msg); break;
			case TxTypes.Call_Multi: w.Write((TxMsgCall_Multi)msg); break;
			case TxTypes.Trade: w.Write((TxMsgTrade)msg); break;
			case TxTypes.TransferFungible: w.Write((TxMsgTransferFungible)msg); break;
			case TxTypes.TransferFungible_GasPayer: w.Write((TxMsgTransferFungible_GasPayer)msg); break;
			case TxTypes.TransferNonFungible_Single: w.Write((TxMsgTransferNonFungible_Single)msg); break;
			case TxTypes.TransferNonFungible_Single_GasPayer: w.Write((TxMsgTransferNonFungible_Single_GasPayer)msg); break;
			case TxTypes.TransferNonFungible_Multi: w.Write((TxMsgTransferNonFungible_Multi)msg); break;
			case TxTypes.TransferNonFungible_Multi_GasPayer: w.Write((TxMsgTransferNonFungible_Multi_GasPayer)msg); break;
			case TxTypes.MintFungible: w.Write((TxMsgMintFungible)msg); break;
			case TxTypes.BurnFungible: w.Write((TxMsgBurnFungible)msg); break;
			case TxTypes.BurnFungible_GasPayer: w.Write((TxMsgBurnFungible_GasPayer)msg); break;
			case TxTypes.MintNonFungible: w.Write((TxMsgMintNonFungible)msg); break;
			case TxTypes.BurnNonFungible: w.Write((TxMsgBurnNonFungible)msg); break;
			case TxTypes.BurnNonFungible_GasPayer: w.Write((TxMsgBurnNonFungible_GasPayer)msg); break;
			case TxTypes.Phantasma: w.Write((TxMsgPhantasma)msg); break;
			case TxTypes.Phantasma_Raw: w.Write((TxMsgPhantasma_Raw)msg); break;
			default: Throw.Assert(false); break;
		}
	}

	public void Write(BinaryWriter w)
	{
		w.Write1(type);
		w.Write8(expiry);
		w.Write8(maxGas);
		w.Write8(maxData);
		w.Write32(gasFrom);
		w.Write(payload);

		WriteDataByType(w, type, msg);
	}

	static object? ReadDataByType(BinaryReader r, TxTypes type)
	{
		object? msg = null;
		switch (type)
		{
			case TxTypes.Call: msg = r.Read<TxMsgCall>(); break;
			case TxTypes.Call_Multi: msg = r.Read<TxMsgCall_Multi>(); break;
			case TxTypes.Trade: msg = r.Read<TxMsgTrade>(); break;
			case TxTypes.TransferFungible: msg = r.Read<TxMsgTransferFungible>(); break;
			case TxTypes.TransferFungible_GasPayer: msg = r.Read<TxMsgTransferFungible_GasPayer>(); break;
			case TxTypes.TransferNonFungible_Single: msg = r.Read<TxMsgTransferNonFungible_Single>(); break;
			case TxTypes.TransferNonFungible_Single_GasPayer: msg = r.Read<TxMsgTransferNonFungible_Single_GasPayer>(); break;
			case TxTypes.TransferNonFungible_Multi: msg = r.Read<TxMsgTransferNonFungible_Multi>(); break;
			case TxTypes.TransferNonFungible_Multi_GasPayer: msg = r.Read<TxMsgTransferNonFungible_Multi_GasPayer>(); break;
			case TxTypes.MintFungible: msg = r.Read<TxMsgMintFungible>(); break;
			case TxTypes.BurnFungible: msg = r.Read<TxMsgBurnFungible>(); break;
			case TxTypes.BurnFungible_GasPayer: msg = r.Read<TxMsgBurnFungible_GasPayer>(); break;
			case TxTypes.MintNonFungible: msg = r.Read<TxMsgMintNonFungible>(); break;
			case TxTypes.BurnNonFungible: msg = r.Read<TxMsgBurnNonFungible>(); break;
			case TxTypes.BurnNonFungible_GasPayer: msg = r.Read<TxMsgBurnNonFungible_GasPayer>(); break;
			case TxTypes.Phantasma: msg = r.Read<TxMsgPhantasma>(); break;
			case TxTypes.Phantasma_Raw: msg = r.Read<TxMsgPhantasma_Raw>(); break;
			default: Throw.Assert(false); break;
		}

		return msg;
	}

	public void Read(BinaryReader r)
	{
		r.Read1(out type);
		r.Read8(out expiry);
		r.Read8(out maxGas);
		r.Read8(out maxData);
		r.Read32(out gasFrom);
		r.Read(out payload);

		msg = ReadDataByType(r, type)!; // It's never null, limitation of netstandard 2
	}

	public static TxMsg FromPhantasmaRaw(byte[] rawTransaction)
	{
		return new TxMsg
		{
			type = TxTypes.Phantasma_Raw,
			expiry = 0,
			maxGas = 0,
			maxData = 0,
			gasFrom = Bytes32.Empty,
			payload = SmallString.Empty,
			msg = new TxMsgPhantasma_Raw
			{
				transaction = rawTransaction,
			},
		};
	}
}
