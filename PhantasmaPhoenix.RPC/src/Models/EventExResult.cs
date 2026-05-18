using PhantasmaPhoenix.Protocol;
using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Models;

[JsonConverter(typeof(EventExResultJsonConverter))]
public class EventExResult
{
	public string Address { get; set; } = string.Empty;

	public string Contract { get; set; } = string.Empty;

	public EventKind Kind { get; set; }

	public object? Data { get; set; }

	public EventExResult()
	{
	}

	public EventExResult(string address, string contract, EventKind kind, object? data)
	{
		Address = address;
		Contract = contract;
		Kind = kind;
		Data = data;
	}
}
