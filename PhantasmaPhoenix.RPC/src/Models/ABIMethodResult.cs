using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class ABIMethodResult
{
	[ApiDescription("Name of method")]
	public string Name { get; set; }

	public string ReturnType { get; set; }

	[ApiDescription("Type of parameters")]
	public ABIParameterResult[] Parameters { get; set; }
}
