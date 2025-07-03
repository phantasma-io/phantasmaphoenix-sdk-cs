using Newtonsoft.Json;
using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol;

public struct GasConfig : ISerializable
{
	public byte version;
	public byte MaxNameLength;
	public byte MaxTokenSymbolLength;
	public byte FeeShift;
	public uint MaxStructureSize;
	public ulong FeeMultiplier;
	public ulong GasTokenId;
	public ulong DataTokenId;
	public ulong MinimumGasOffer;
	public ulong DataEscrowPerRow;
	public ulong GasFeeTransfer;
	public ulong GasFeeQuery;
	public ulong GasFeeCreateTokenBase;
	public ulong GasFeeCreateTokenSymbol;
	public ulong GasFeeCreateTokenSeries;
	public ulong GasFeePerByte;
	public ulong GasFeeRegisterName;
	public ulong GasBurnRatioMul;
	public byte GasBurnRatioShift;

	public static byte[] Serialize(GasConfig? gasConfig)
	{
		if (gasConfig == null)
		{
			return Array.Empty<byte>();
		}

		using MemoryStream stream = new MemoryStream();
		using BinaryWriter writer = new BinaryWriter(stream);
		gasConfig.Value.SerializeData(writer);
		writer.Flush();
		return stream.ToArray();
	}

	public void SerializeData(BinaryWriter writer)
	{
		writer.Write(this.version);
		writer.Write(this.MaxNameLength);
		writer.Write(this.MaxTokenSymbolLength);
		writer.Write(this.FeeShift);
		writer.Write(this.MaxStructureSize);
		writer.Write(this.FeeMultiplier);
		writer.Write(this.GasTokenId);
		writer.Write(this.DataTokenId);
		writer.Write(this.MinimumGasOffer);
		writer.Write(this.DataEscrowPerRow);
		writer.Write(this.GasFeeTransfer);
		writer.Write(this.GasFeeQuery);
		writer.Write(this.GasFeeCreateTokenBase);
		writer.Write(this.GasFeeCreateTokenSymbol);
		writer.Write(this.GasFeeCreateTokenSeries);
		writer.Write(this.GasFeePerByte);
		writer.Write(this.GasFeeRegisterName);
		writer.Write(this.GasBurnRatioMul);
		writer.Write(this.GasBurnRatioShift);
	}

	public static GasConfig? Unserialize(byte[] bytes)
	{
		using (var stream = new MemoryStream(bytes))
		{
			using (var reader = new BinaryReader(stream))
			{
				return Unserialize(reader);
			}
		}
	}

	public static GasConfig? Unserialize(BinaryReader reader)
	{
		var evnt = new GasConfig();
		try
		{
			evnt.UnserializeData(reader);
			return evnt;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public void UnserializeData(BinaryReader reader)
	{
		this.version = reader.ReadByte();
		this.MaxNameLength = reader.ReadByte();
		this.MaxTokenSymbolLength = reader.ReadByte();
		this.FeeShift = reader.ReadByte();
		this.MaxStructureSize = reader.ReadUInt32();
		this.FeeMultiplier = reader.ReadUInt64();
		this.GasTokenId = reader.ReadUInt64();
		this.DataTokenId = reader.ReadUInt64();
		this.MinimumGasOffer = reader.ReadUInt64();
		this.DataEscrowPerRow = reader.ReadUInt64();
		this.GasFeeTransfer = reader.ReadUInt64();
		this.GasFeeQuery = reader.ReadUInt64();
		this.GasFeeCreateTokenBase = reader.ReadUInt64();
		this.GasFeeCreateTokenSymbol = reader.ReadUInt64();
		this.GasFeeCreateTokenSeries = reader.ReadUInt64();
		this.GasFeePerByte = reader.ReadUInt64();
		this.GasFeeRegisterName = reader.ReadUInt64();
		this.GasBurnRatioMul = reader.ReadUInt64();
		this.GasBurnRatioShift = reader.ReadByte();
	}

	public override string ToString()
	{
		return JsonConvert.SerializeObject(this, Formatting.Indented);
	}
}
