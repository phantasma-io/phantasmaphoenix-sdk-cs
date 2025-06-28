namespace PhantasmaPhoenix.Core.Extensions;

public static class IDictionaryExtensions
{
	public static void Merge<K, V>(this IDictionary<K, V> target, IDictionary<K, V> source)
	{
		source.ToList().ForEach(_ =>
		{
			if (!target.ContainsKey(_.Key))
			{
				target[_.Key] = _.Value;
			}
		});
	}
}
