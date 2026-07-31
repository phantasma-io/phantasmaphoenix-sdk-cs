using Newtonsoft.Json;

namespace PhantasmaPhoenix.Core;

/// <summary>
/// Reads and writes <see cref="HexBytes"/> as the plain hex string it has always been on the wire.
/// </summary>
/// <remarks>
/// When the writer can stream a string value in pieces (<see cref="IHexValueWriter"/>), the hex is
/// written straight from the bytes; otherwise the string is built as before. Both paths produce the
/// same characters, so no consumer can tell which one ran.
/// </remarks>
public class HexBytesJsonConverter : JsonConverter<HexBytes?>
{
	public override void WriteJson(JsonWriter writer, HexBytes? value, JsonSerializer serializer)
	{
		if (value == null)
		{
			writer.WriteNull();
			return;
		}

		if (writer is IHexValueWriter hexWriter)
		{
			value.WriteTo(hexWriter);
			return;
		}

		writer.WriteValue(value.ToString());
	}

	public override HexBytes? ReadJson(JsonReader reader, Type objectType, HexBytes? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null) return null;

		var hex = reader.Value as string;
		if (hex == null)
		{
			throw new JsonSerializationException($"Expected a hex string, got {reader.TokenType}.");
		}

		return HexBytes.FromHex(hex);
	}
}
