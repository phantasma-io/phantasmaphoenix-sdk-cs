using PhantasmaPhoenix.Core.Extensions;
using PhantasmaPhoenix.Protocol;

namespace PhantasmaPhoenix.RPC.Models;

public class EventResult
{
	public string Address { get; set; }
	public string Contract { get; set; }
	public string Kind { get; set; }
	public string Name { get; set; }
	public string Data { get; set; }

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
