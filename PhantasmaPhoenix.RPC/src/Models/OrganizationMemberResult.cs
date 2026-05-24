using PhantasmaPhoenix.RPC.Annotations;

namespace PhantasmaPhoenix.RPC.Models;

public class OrganizationMemberResult
{
	[ApiDescription("Member formatted as a Phantasma address.")]
	public string Address { get; set; } = string.Empty;

	[ApiDescription("Raw 32-byte Carbon member key encoded as lowercase hex.")]
	public string CarbonAddress { get; set; } = string.Empty;

	public bool IsMember { get; set; } = true;

	[ApiDescription("Unix millisecond timestamp when the address joined the organization. Omitted when not requested or not a member.")]
	public long? MemberTime { get; set; }

	public OrganizationMemberResult() { }
}
