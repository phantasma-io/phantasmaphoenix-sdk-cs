using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ABIParameterResult
{
	[ApiDescription("Name of method")]
	public string Name { get; set; }

	public string Type { get; set; }

	public ABIParameterResult() { }
}
