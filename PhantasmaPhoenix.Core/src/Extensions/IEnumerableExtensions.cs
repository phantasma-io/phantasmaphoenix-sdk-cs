using System.Collections;

namespace PhantasmaPhoenix.Core.Extensions;

public static class IEnumerableExtensions
{
	public static void ForEach<T>(this IEnumerable<T> collection, Action<T> action)
	{
		foreach (var item in collection)
		{
			action(item);
		}
	}

	/// <summary>
	/// ForEach with an index.
	/// </summary>
	public static void ForEachWithIndex<T>(this IEnumerable<T> collection, Action<T, int> action)
	{
		int n = 0;

		foreach (var item in collection)
		{
			action(item, n++);
		}
	}

	/// <summary>
	/// Implements ForEach for non-generic enumerators.
	/// </summary>
	// Usage: Controls.ForEach<Control>(t=>t.DoSomething());
	public static void ForEach<T>(this IEnumerable collection, Action<T> action)
	{
		foreach (T item in collection)
		{
			action(item);
		}
	}

	public static IEnumerable<T> ExceptBy<T, TKey>(this IEnumerable<T> src, T item, Func<T, TKey> keySelector)
	{
		TKey itemKey = keySelector(item);

		using (var enumerator = src.GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				T current = enumerator.Current;

				if (!keySelector(current).Equals(itemKey))
				{
					yield return current;
				}
			}
		}
	}

	public static IEnumerable<T> ExceptBy<T, TKey>(this IEnumerable<T> src, IEnumerable<T> items,
		Func<T, TKey> keySelector)
	{
		using (var enumerator = src.GetEnumerator())
		{
			while (enumerator.MoveNext())
			{
				T current = enumerator.Current;

				if (items.None(i => keySelector(current).Equals(keySelector(i))))
				{
					yield return current;
				}
			}
		}
	}

	public static bool None<TSource>(this IEnumerable<TSource> source)
	{
		return !source.Any();
	}

	public static bool None<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
	{
		return !source.Any(predicate);
	}

	// Welford's method: https://mathoverflow.net/questions/70345/numerically-most-robust-way-to-compute-sum-of-products-standard-deviation-in-f
	// From: https://stackoverflow.com/questions/2253874/standard-deviation-in-linq
	public static double StdDev(this IEnumerable<double> values)
	{
		double mean = 0.0;
		double sum = 0.0;
		double stdDev = 0.0;
		int n = 0;
		foreach (double val in values)
		{
			n++;
			double delta = val - mean;
			mean += delta / n;
			sum += delta * (val - mean);
		}

		if (1 < n)
			stdDev = System.Math.Sqrt(sum / (n - 1));

		return stdDev;
	}
}
