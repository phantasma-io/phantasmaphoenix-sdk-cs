using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol;
using Newtonsoft.Json;

namespace PhantasmaPhoenix.RPC.Models;

[JsonConverter(typeof(EventResultJsonConverter))]
public class EventResult
{
	public string Address { get; set; } = string.Empty;
	public string Contract { get; set; } = string.Empty;
	public string Kind { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Data { get; set; } = string.Empty;

	public EventResult() { }

	public EventResult(string address, string contract, string kind, string name, string data)
	{
		Address = address;
		Contract = contract;
		Kind = kind;
		Name = name;
		Data = data;
	}

	public EventResult(Event e)
	{
		Address = e.Address.ToString();
		Contract = e.Contract;
		Kind = e.Kind.ToString();
		Name = e.Name != null ? e.Name : e.Kind.ToString();
		Data = e.Data.ToHex();
	}
}
