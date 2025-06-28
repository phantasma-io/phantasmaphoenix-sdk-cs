using PhantasmaPhoenix.Core;

namespace PhantasmaPhoenix.Protocol;

public static class DomainExtensions
{
	public static T GetContent<T>(this Event evt)
	{
		return Serialization.Unserialize<T>(evt.Data);
	}
}
