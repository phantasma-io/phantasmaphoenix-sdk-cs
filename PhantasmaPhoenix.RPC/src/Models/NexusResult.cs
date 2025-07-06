using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class NexusResult
{
	[ApiDescription("Name of the nexus")]
	public string Name { get; set; }

	[ApiDescription("Network protocol version")]
	public uint Protocol { get; set; }

	// TODO Commented: PlatformResult[], should we still implement it somehow?
	// [ApiDescription("List of platforms")]
	// public PlatformResult[] platforms { get; set; }

	[ApiDescription("List of tokens")]
	public TokenResult[] Tokens { get; set; }

	[ApiDescription("List of chains")]
	public ChainResult[] Chains { get; set; }

	[ApiDescription("List of governance values")]
	public GovernanceResult[] Governance { get; set; }

	[ApiDescription("List of organizations")]
	public string[] Organizations { get; set; }
}
