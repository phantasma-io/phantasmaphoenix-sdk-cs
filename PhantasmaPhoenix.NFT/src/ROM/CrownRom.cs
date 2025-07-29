using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Cryptography.Extensions;

namespace PhantasmaPhoenix.NFT;

public class CrownRom : IRom
{
	private readonly bool isEmpty;
	private readonly string? parsingError;
	public string tokenId;
	public Address staker;
	public Timestamp date;

	public CrownRom(byte[] rom, string tokenId)
	{
		this.tokenId = tokenId;

		if (rom == null || rom.Length == 0)
		{
			isEmpty = true;
			return;
		}

		try
		{
			using (var stream = new System.IO.MemoryStream(rom))
			{
				using (var reader = new System.IO.BinaryReader(stream))
				{
					UnserializeData(reader);
				}
			}
		}
		catch (Exception e)
		{
			parsingError = $"Cannot parse ROM '{System.Text.Encoding.ASCII.GetString(rom)}/{BitConverter.ToString(rom)}': {e.Message}";
		}
	}

	public bool IsEmpty() => isEmpty;
	public (bool, string?) HasParsingError() => (!string.IsNullOrEmpty(parsingError), parsingError);

	public string GetName() => "CROWN #" + tokenId;
	public string GetDescription() => "";
	public DateTime GetDate() => date;

	private void UnserializeData(System.IO.BinaryReader reader)
	{
		this.staker = reader.ReadAddress();
		this.date = new Timestamp(reader.ReadUInt32());
	}
}
