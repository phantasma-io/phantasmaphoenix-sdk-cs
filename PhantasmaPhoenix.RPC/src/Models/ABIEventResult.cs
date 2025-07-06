using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ABIEventResult
{
	[ApiDescription("Value of event")]
	public int Value { get; set; }

	[ApiDescription("Name of event")]
	public string Name { get; set; }

	public string ReturnType { get; set; }

	[ApiDescription("Description script (base16 encoded)")]
	public string Description { get; set; }
}
