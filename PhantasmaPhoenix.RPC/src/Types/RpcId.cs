using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.RPC.Types;

/// <summary>
/// JSON-RPC request/response id. The protocol allows string, integer, or null ids.
/// </summary>
[JsonConverter(typeof(RpcIdJsonConverter))]
public sealed class RpcId : IEquatable<RpcId>
{
	private readonly JToken _value;

	private RpcId(JToken value)
	{
		Validate(value);
		_value = value.DeepClone();
	}

	public RpcId(string value)
	{
		if (value == null)
		{
			throw new ArgumentNullException(nameof(value));
		}

		_value = new JValue(value);
	}

	public RpcId(long value)
	{
		_value = new JValue(value);
	}

	public RpcId(int value)
		: this((long)value)
	{
	}

	public JToken Value => _value.DeepClone();

	public static RpcId FromJsonToken(JToken value)
	{
		return new RpcId(value);
	}

	public static implicit operator RpcId(string value)
	{
		return new RpcId(value);
	}

	public static implicit operator RpcId(long value)
	{
		return new RpcId(value);
	}

	public static implicit operator RpcId(int value)
	{
		return new RpcId(value);
	}

	public bool Equals(RpcId? other)
	{
		return other != null && JToken.DeepEquals(_value, other._value);
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as RpcId);
	}

	public override int GetHashCode()
	{
		return _value.ToString(Formatting.None).GetHashCode();
	}

	public override string ToString()
	{
		return _value.Type == JTokenType.String
			? _value.Value<string>()!
			: _value.ToString(Formatting.None);
	}

	internal void WriteTo(JsonWriter writer)
	{
		_value.WriteTo(writer);
	}

	private static void Validate(JToken value)
	{
		switch (value.Type)
		{
			case JTokenType.String:
			case JTokenType.Integer:
				return;
			default:
				throw new JsonSerializationException($"JSON-RPC id must be a string, integer, or null, got {value.Type}");
		}
	}
}

internal sealed class RpcIdJsonConverter : JsonConverter
{
	public override bool CanConvert(Type objectType)
	{
		return objectType == typeof(RpcId);
	}

	public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
		{
			return null;
		}

		return RpcId.FromJsonToken(JToken.Load(reader));
	}

	public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
	{
		if (value == null)
		{
			writer.WriteNull();
			return;
		}

		((RpcId)value).WriteTo(writer);
	}
}
