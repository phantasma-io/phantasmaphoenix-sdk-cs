namespace PhantasmaPhoenix.Core.Extensions;

public static class IntExtensions
{
	public static void ForEach(this int n, Action action)
	{
		for (int i = 0; i < n; i++)
		{
			action();
		}
	}

	public static void ForEach(this int n, Action<int> action)
	{
		for (int i = 0; i < n; i++)
		{
			action(i);
		}
	}

	public static IEnumerable<int> Range(this int n)
	{
		return Enumerable.Range(0, n);
	}
}
