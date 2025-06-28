namespace PhantasmaPhoenix.Core.Extensions;

// TODO why not IList?
public static class ListExtensions
{
	public static void MoveToTail<T>(this List<T> list, T item, Predicate<T> pred)
	{
		int idx = list.FindIndex(pred);

		if (idx == -1)
		{
			return;
		}

		list.RemoveAt(idx);
		list.Add(item);
	}

	public static void AddMaximum<T>(this List<T> list, T item, int max)
	{
		list.Add(item);

		if (list.Count > max)
		{
			list.RemoveAt(0);
		}
	}

	public static void AddDistinct<T>(this List<T> list, T item)
	{
		if (!list.Contains(item))
		{
			list.Add(item);
		}
	}

	public static bool ContainsBy<T, TKey>(this List<T> list, T item, Func<T, TKey> keySelector)
	{
		TKey itemKey = keySelector(item);

		return list.Any(n => keySelector(n).Equals(itemKey));
	}

	public static void AddDistinctBy<T, TKey>(this List<T> list, T item, Func<T, TKey> keySelector)
	{
		TKey itemKey = keySelector(item);

		// no items in the list must match the item.
		if (list.None(q => keySelector(q).Equals(itemKey)))
		{
			list.Add(item);
		}
	}

	// TODO: Change the equalityComparer to a KeySelector for the these extension methods:
	public static void AddRangeDistinctBy<T>(this List<T> target, IEnumerable<T> src,
		Func<T, T, bool> equalityComparer)
	{
		src.ForEach(item =>
		{
			// no items in the list must match the item.
			if (target.None(q => equalityComparer(q, item)))
			{
				target.Add(item);
			}
		});
	}

	public static void RemoveRange<T>(this List<T> target, List<T> src)
	{
		src.ForEach(s => target.Remove(s));
	}

	public static void RemoveRange<T>(this List<T> target, List<T> src, Func<T, T, bool> equalityComparer)
	{
		src.ForEach(s =>
		{
			int idx = target.FindIndex(t => equalityComparer(t, s));

			if (idx != -1)
			{
				target.RemoveAt(idx);
			}
		});
	}

	private static Random rng = new Random();

	public static void Shuffle<T>(this IList<T> list)
	{
		int n = list.Count;
		while (n > 1)
		{
			n--;
			int k = rng.Next(n + 1);
			T value = list[k];
			list[k] = list[n];
			list[n] = value;
		}
	}
}
