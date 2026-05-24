using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class OrganizationResult
{
	[ApiDescription("Numeric Carbon organization ID, serialized as text to preserve UInt64 precision in JSON clients.")]
	public string Id { get; set; } = string.Empty;

	public string Name { get; set; } = string.Empty;

	[ApiDescription("Organization owner formatted as a Phantasma address.")]
	public string Owner { get; set; } = string.Empty;

	[ApiDescription("Raw 32-byte Carbon organization owner key encoded as lowercase hex.")]
	public string CarbonOwner { get; set; } = string.Empty;

	public TokenPropertyResult[] Metadata { get; set; } = Array.Empty<TokenPropertyResult>();

	[ApiDescription("Organization member count. Returned only when explicitly requested because it requires a member range scan.")]
	public string? MemberCount { get; set; }

	public OrganizationResult() { }
}
