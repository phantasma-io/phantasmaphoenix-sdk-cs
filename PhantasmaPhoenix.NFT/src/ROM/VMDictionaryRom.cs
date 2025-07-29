using PhantasmaPhoenix.VM;

namespace PhantasmaPhoenix.NFT;

public class VMDictionaryRom : IRom
{
	private readonly bool isEmpty;
	private readonly string? parsingError;
	private readonly Dictionary<VMObject, VMObject> fields = new();

	public VMDictionaryRom(byte[] romBytes)
	{
		if (romBytes == null || romBytes.Length == 0)
		{
			isEmpty = true;
			return;
		}

		try
		{
			var rom = VMObject.FromBytes(romBytes);
			if (rom.Type == VMType.Struct)
			{
				fields = (Dictionary<VMObject, VMObject>)rom.Data;
			}
			else
			{
				parsingError = $"Cannot parse ROM '{System.Text.Encoding.ASCII.GetString(romBytes)}/{BitConverter.ToString(romBytes)}': Unsupported ROM type '{rom.Type}'";
			}
		}
		catch (Exception e)
		{
			parsingError = $"Cannot parse ROM '{System.Text.Encoding.ASCII.GetString(romBytes)}/{BitConverter.ToString(romBytes)}': {e.Message}";
		}
	}

	public bool IsEmpty() => isEmpty;
	public (bool, string?) HasParsingError() => (!string.IsNullOrEmpty(parsingError), parsingError);

	public string GetName()
	{
		if (fields.TryGetValue(VMObject.FromObject("name"), out var value))
		{
			return value.AsString();
		}
		return "";
	}
	public string GetDescription()
	{
		if (fields.TryGetValue(VMObject.FromObject("description"), out var value))
		{
			return value.AsString();
		}
		return "";
	}
	public DateTime GetDate()
	{
		if (fields.TryGetValue(VMObject.FromObject("created"), out var value))
		{
			return value.AsTimestamp();
		}
		return new DateTime();
	}
}
