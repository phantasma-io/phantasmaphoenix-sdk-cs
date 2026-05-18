using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PhantasmaPhoenix.RPC.Models;

internal abstract class StaleRpcFieldIgnoringJsonConverter<T> : JsonConverter<T> where T : class, new()
{
	protected abstract string[] StaleFieldNames { get; }

	public override bool CanWrite => false;

	public override T? ReadJson(JsonReader reader, Type objectType, T? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.Null)
			return null;

		var obj = JObject.Load(reader);
		foreach (var prop in obj.Properties().ToArray())
		{
			if (Array.IndexOf(StaleFieldNames, prop.Name) >= 0)
				prop.Remove();
		}

		var result = hasExistingValue && existingValue != null ? existingValue : new T();
		using var subReader = obj.CreateReader();
		serializer.Populate(subReader, result);
		return result;
	}

	public override void WriteJson(JsonWriter writer, T? value, JsonSerializer serializer)
	{
		throw new NotSupportedException();
	}
}

internal sealed class EventResultJsonConverter : StaleRpcFieldIgnoringJsonConverter<EventResult>
{
	protected override string[] StaleFieldNames { get; } = { "Kind", "Data" };
}

internal sealed class EventExResultJsonConverter : StaleRpcFieldIgnoringJsonConverter<EventExResult>
{
	protected override string[] StaleFieldNames { get; } = { "Kind", "Data" };
}

internal sealed class TransactionSignatureResultJsonConverter : StaleRpcFieldIgnoringJsonConverter<TransactionSignatureResult>
{
	protected override string[] StaleFieldNames { get; } = { "Kind", "Data" };
}

internal sealed class TokenPropertyResultJsonConverter : StaleRpcFieldIgnoringJsonConverter<TokenPropertyResult>
{
	protected override string[] StaleFieldNames { get; } = { "Key", "Value" };
}

internal sealed class TokenDataResultJsonConverter : StaleRpcFieldIgnoringJsonConverter<TokenDataResult>
{
	protected override string[] StaleFieldNames { get; } = { "ID" };
}

internal sealed class TokenResultJsonConverter : StaleRpcFieldIgnoringJsonConverter<TokenResult>
{
	protected override string[] StaleFieldNames { get; } = { "carbonID" };
}
