using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ChainResult
{
	public string Name { get; set; }
	public string Address { get; set; }

	[ApiDescription("Name of parent chain")]
	public string Parent { get; set; }

	[ApiDescription("Current chain height")]
	public uint Height { get; set; }

	[ApiDescription("Chain organization")]
	public string Organization { get; set; }

	[ApiDescription("Contracts deployed in the chain")]
	public string[] Contracts { get; set; }

	[ApiDescription("Dapps deployed in the chain")]
	public string[] Dapps { get; set; }

	public ChainResult() { }
}
