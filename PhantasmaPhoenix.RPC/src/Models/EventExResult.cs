using PhantasmaPhoenix.Protocol;

namespace PhantasmaPhoenix.RPC.Models;

public class EventExResult
{
	public string Address { get; set; }

	public string Contract { get; set; }

	public EventKind Kind { get; set; }

	public object Data { get; set; }

	public EventExResult()
	{
	}

	public EventExResult(string address, string contract, EventKind kind, object data)
	{
		Address = address;
		Contract = contract;
		Kind = kind;
		Data = data;
	}
}
