using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ContractResult
{
	[ApiDescription("Name of contract")]
	public string Name { get; set; }

	[ApiDescription("Address of contract")]
	public string Address { get; set; }

	[ApiDescription("Script bytes, in hex format")]
	public string Script { get; set; }

	[ApiDescription("List of methods")]
	public ABIMethodResult[] Methods { get; set; }

	[ApiDescription("List of events")]
	public ABIEventResult[] Events { get; set; }

	public ContractResult() { }
}

