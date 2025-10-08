#if NET6_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace PhantasmaPhoenix.Protocol.Carbon.Blockchain.Vm;

public static class VmGlobal
{
	public static void VmAssert(
#if NET6_0_OR_GREATER
	[DoesNotReturnIf(false)]
#endif
	bool condition, string constraintName = "")
	{
		if (!condition)
			throw new VmException("Assertion failed: " + constraintName); // todo different type
	}
	public static void VmExpect(
#if NET6_0_OR_GREATER
		[DoesNotReturnIf(false)]
#endif
	bool condition, string constraintName)
	{
		if (!condition)
			throw new VmException("Constraint failed: " + constraintName); // todo different type
	}

#if NET6_0_OR_GREATER
	[DoesNotReturn]
#endif
	public static void VmError(string constraintName)
	{
		throw new VmException("Constraint failed: " + constraintName); // todo different type
	}
}
