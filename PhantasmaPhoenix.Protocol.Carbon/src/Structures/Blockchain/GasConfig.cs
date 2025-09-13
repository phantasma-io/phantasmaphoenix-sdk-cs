namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct GasConfig : ICarbonBlob
{
	public Byte   version;
	public Byte   maxNameLength;
	public Byte   maxTokenSymbolLength;
	public Byte   feeShift;
	public UInt32 maxStructureSize;
	public UInt64 feeMultiplier;
	public UInt64 gasTokenId;
	public UInt64 dataTokenId;
	public UInt64 minimumGasOffer;
	public UInt64 dataEscrowPerRow;
	public UInt64 gasFeeTransfer;
	public UInt64 gasFeeQuery;
	public UInt64 gasFeeCreateTokenBase;
	public UInt64 gasFeeCreateTokenSymbol;
	public UInt64 gasFeeCreateTokenSeries;
	public UInt64 gasFeePerByte;
	public UInt64 gasFeeRegisterName;
	public UInt64 gasBurnRatioMul;
	public Byte   gasBurnRatioShift;

	public void Write(BinaryWriter w)
	{
		w.Write1(version);
		w.Write1(maxNameLength);
		w.Write1(maxTokenSymbolLength);
		w.Write1(feeShift);
		w.Write4(maxStructureSize);
		w.Write8(feeMultiplier);
		w.Write8(gasTokenId);
		w.Write8(dataTokenId);
		w.Write8(minimumGasOffer);
		w.Write8(dataEscrowPerRow);
		w.Write8(gasFeeTransfer);
		w.Write8(gasFeeQuery);
		w.Write8(gasFeeCreateTokenBase);
		w.Write8(gasFeeCreateTokenSymbol);
		w.Write8(gasFeeCreateTokenSeries);
		w.Write8(gasFeePerByte);
		w.Write8(gasFeeRegisterName);
		w.Write8(gasBurnRatioMul);
		w.Write1(gasBurnRatioShift);
	}

	public void Read(BinaryReader r)
	{
		r.Read1(out version);
		r.Read1(out maxNameLength);
		r.Read1(out maxTokenSymbolLength);
		r.Read1(out feeShift);
		r.Read4(out maxStructureSize);
		r.Read8(out feeMultiplier);
		r.Read8(out gasTokenId);
		r.Read8(out dataTokenId);
		r.Read8(out minimumGasOffer);
		r.Read8(out dataEscrowPerRow);
		r.Read8(out gasFeeTransfer);
		r.Read8(out gasFeeQuery);
		r.Read8(out gasFeeCreateTokenBase);
		r.Read8(out gasFeeCreateTokenSymbol);
		r.Read8(out gasFeeCreateTokenSeries);
		r.Read8(out gasFeePerByte);
		r.Read8(out gasFeeRegisterName);
		r.Read8(out gasBurnRatioMul);
		r.Read1(out gasBurnRatioShift);
	}
}
