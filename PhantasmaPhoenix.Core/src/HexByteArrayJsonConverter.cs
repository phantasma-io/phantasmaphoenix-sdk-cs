using Newtonsoft.Json;

namespace PhantasmaPhoenix.Core;

public class HexByteArrayJsonConverter : JsonConverter<byte[]>
{
	public override void WriteJson(JsonWriter writer, byte[]? value, JsonSerializer serializer)
	{
		writer.WriteValue(BitConverter.ToString(value).Replace("-", "").ToLower());
	}

	public override byte[] ReadJson(JsonReader reader, Type objectType, byte[]? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		string? hex = (string?)reader.Value;
		if (hex == null)
		{
			return new byte[] { };
		}

		return Enumerable.Range(0, hex.Length / 2)
						 .Select(x => Convert.ToByte(hex.Substring(x * 2, 2), 16))
						 .ToArray();
	}
}
