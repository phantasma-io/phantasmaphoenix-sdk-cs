namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain;

public struct GasConfig : ICarbonBlob
{
	public Byte version;
	public Byte maxNameLength;
	public Byte maxTokenSymbolLength;
	public Byte feeShift;
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
	public Byte gasBurnRatioShift;

	// Gas-model-v2 extension, serialized only for version >= 1 (mirrors the node's
	// data_blockchain.h wire format exactly). The version-0 byte image is frozen forever for
	// historical replay; a version>=1 image truncated to the v0 length must fail to parse
	// (the tail read below throws on end of stream).

	// Floor applied to every settled gas bill (kcal-base). 0 = no floor (v1-equivalent).
	public UInt64 minimumGasBill;
	// Fee split: bill portions credited to the block producer and to the tx gasTarget dapp
	// address. Same mul/shift fixed-point form as the burn ratio; 0/0 = v1-equivalent.
	public UInt64 gasProducerRatioMul;
	public Byte gasProducerRatioShift;
	public UInt64 gasDappRatioMul;
	public Byte gasDappRatioShift;
	// Product-decision prices ("policy fees") in kcal-base, charged directly (no fee multiplier
	// stage). Under v2 they replace the unit-priced gasFeeCreateToken*/gasFeeRegisterName fields,
	// which stay serialized for version-0 replay.
	public UInt64 policyFeeCreateTokenBase;
	// Halved per symbol char after the first (v1 rule kept).
	public UInt64 policyFeeCreateTokenSymbol;
	public UInt64 policyFeeCreateTokenSeries;
	// Shifted right by (nameLength-1) like the v1 field (v1 rule kept).
	public UInt64 policyFeeRegisterName;
	// The frozen pre-flip dataEscrowPerRow: storage rows existing before the v2 flip refund at
	// this price (exactly what they escrowed under v1). Immutable after the flip.
	public UInt64 legacyDataEscrowPerRow;

	/// <summary>
	/// True when this config activates the gas-model-v2 billing rules (config version >= 1).
	/// The gas model is gated by the config version, not by a chain feature level.
	/// </summary>
	public bool HasGasModelV2 => version >= 1;

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
		if (version == 0)
		{
			// Version-0 wire image must stay byte-identical to the pre-v2 layout.
			return;
		}
		w.Write8(minimumGasBill);
		w.Write8(gasProducerRatioMul);
		w.Write1(gasProducerRatioShift);
		w.Write8(gasDappRatioMul);
		w.Write1(gasDappRatioShift);
		w.Write8(policyFeeCreateTokenBase);
		w.Write8(policyFeeCreateTokenSymbol);
		w.Write8(policyFeeCreateTokenSeries);
		w.Write8(policyFeeRegisterName);
		w.Write8(legacyDataEscrowPerRow);
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
		if (version == 0)
		{
			// Version-0 rows carry no v2 tail. Zero it explicitly so the parsed value does not
			// depend on the caller having a fresh struct.
			minimumGasBill = 0;
			gasProducerRatioMul = 0;
			gasProducerRatioShift = 0;
			gasDappRatioMul = 0;
			gasDappRatioShift = 0;
			policyFeeCreateTokenBase = 0;
			policyFeeCreateTokenSymbol = 0;
			policyFeeCreateTokenSeries = 0;
			policyFeeRegisterName = 0;
			legacyDataEscrowPerRow = 0;
			return;
		}
		// version >= 1: the tail is mandatory; a truncated image throws (end of stream).
		r.Read8(out minimumGasBill);
		r.Read8(out gasProducerRatioMul);
		r.Read1(out gasProducerRatioShift);
		r.Read8(out gasDappRatioMul);
		r.Read1(out gasDappRatioShift);
		r.Read8(out policyFeeCreateTokenBase);
		r.Read8(out policyFeeCreateTokenSymbol);
		r.Read8(out policyFeeCreateTokenSeries);
		r.Read8(out policyFeeRegisterName);
		r.Read8(out legacyDataEscrowPerRow);
	}
}
