using PhantasmaPhoenix.Cryptography;

namespace PhantasmaPhoenix.Protocol;

public struct OrganizationEventData
{
	public readonly string Organization;
	public readonly Address MemberAddress;

	public OrganizationEventData(string organization, Address memberAddress)
	{
		this.Organization = organization;
		this.MemberAddress = memberAddress;
	}
}
