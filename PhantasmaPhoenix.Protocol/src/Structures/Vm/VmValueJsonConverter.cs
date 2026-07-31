using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace PhantasmaPhoenix.Protocol;

/// <summary>
/// Maps <see cref="VmValue"/> to and from the plain JSON value it represents.
/// </summary>
/// <remarks>
/// A scalar is written as a JSON string, an array as a JSON array, a struct as a JSON object. Nothing
/// wraps the value in an envelope: the shape itself says which of the three it is, which is what makes
/// the answers readable to consumers that do not use this SDK.
/// </remarks>
internal sealed class VmValueJsonConverter : JsonConverter<VmValue>
{
	public override void WriteJson(JsonWriter writer, VmValue? value, JsonSerializer serializer)
	{
		if (value == null)
		{
			writer.WriteNull();
			return;
		}

		if (value.Fields != null)
		{
			writer.WriteStartObject();
			foreach (var field in value.Fields)
			{
				writer.WritePropertyName(field.Key);
				WriteJson(writer, field.Value, serializer);
			}
			writer.WriteEndObject();
			return;
		}

		if (value.Items != null)
		{
			writer.WriteStartArray();
			foreach (var item in value.Items)
				WriteJson(writer, item, serializer);
			writer.WriteEndArray();
			return;
		}

		writer.WriteValue(value.Text);
	}

	public override VmValue? ReadJson(
		JsonReader reader,
		Type objectType,
		VmValue? existingValue,
		bool hasExistingValue,
		JsonSerializer serializer)
	{
		switch (reader.TokenType)
		{
			case JsonToken.Null:
				return null;

			case JsonToken.StartObject:
				{
					var fields = new Dictionary<string, VmValue>(StringComparer.Ordinal);
					while (reader.Read() && reader.TokenType != JsonToken.EndObject)
					{
						if (reader.TokenType != JsonToken.PropertyName)
							throw new JsonSerializationException($"Unexpected token {reader.TokenType} in a VM struct.");

						var name = (string)reader.Value!;
						reader.Read();
						fields[name] = ReadJson(reader, objectType, null, false, serializer)
							?? VmValue.FromText(string.Empty);
					}

					return VmValue.FromFields(fields);
				}

			case JsonToken.StartArray:
				{
					var items = new List<VmValue>();
					while (reader.Read() && reader.TokenType != JsonToken.EndArray)
					{
						items.Add(ReadJson(reader, objectType, null, false, serializer)
							?? VmValue.FromText(string.Empty));
					}

					return VmValue.FromItems(items.ToArray());
				}

			default:
				// Scalars are written as strings, but a value produced by an older node - or by hand -
				// can still arrive as a JSON number or boolean; both are accepted and normalized.
				return VmValue.FromText(reader.Value?.ToString() ?? string.Empty);
		}
	}
}
