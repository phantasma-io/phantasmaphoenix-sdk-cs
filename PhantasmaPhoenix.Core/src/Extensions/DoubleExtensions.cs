namespace PhantasmaPhoenix.Core.Extensions;

public static class DoubleExtensions
{
	public static bool ApproximatelyEquals(this double d, double val, double range)
	{
		return d >= val - range && d <= val + range;
	}
}
