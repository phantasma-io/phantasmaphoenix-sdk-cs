namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct TxMsgTrade : ICarbonBlob
{
	public TxMsgTransferFungible_GasPayer[] transferF;
	public TxMsgTransferNonFungible_Single_GasPayer[] transferN;
	public TxMsgMintFungible[] mintF;
	public TxMsgBurnFungible_GasPayer[] burnF;
	public TxMsgMintNonFungible[] mintN;
	public TxMsgBurnNonFungible_GasPayer[] burnN;

	public void Write(BinaryWriter w)
	{
		w.WriteArray(transferF);
		w.WriteArray(transferN);
		w.WriteArray(mintF);
		w.WriteArray(burnF);
		w.WriteArray(mintN);
		w.WriteArray(burnN);
	}

	public void Read(BinaryReader r)
	{
		r.ReadArray(out transferF);
		r.ReadArray(out transferN);
		r.ReadArray(out mintF);
		r.ReadArray(out burnF);
		r.ReadArray(out mintN);
		r.ReadArray(out burnN);
	}
}
