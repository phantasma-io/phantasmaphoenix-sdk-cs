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
		// A default-constructed value leaves every array null. Each one is written as an empty array,
		// the way the token call arguments treat theirs. Writing the fields bare would throw instead.
		w.WriteArray(transferF ?? Array.Empty<TxMsgTransferFungible_GasPayer>());
		w.WriteArray(transferN ?? Array.Empty<TxMsgTransferNonFungible_Single_GasPayer>());
		w.WriteArray(mintF ?? Array.Empty<TxMsgMintFungible>());
		w.WriteArray(burnF ?? Array.Empty<TxMsgBurnFungible_GasPayer>());
		w.WriteArray(mintN ?? Array.Empty<TxMsgMintNonFungible>());
		w.WriteArray(burnN ?? Array.Empty<TxMsgBurnNonFungible_GasPayer>());
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
