namespace PhantasmaPhoenix.RPC.Models;

public class OrganizationResult
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string[] Members { get; set; }

	public OrganizationResult() { }
}
