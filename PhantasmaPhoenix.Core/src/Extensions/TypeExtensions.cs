namespace PhantasmaPhoenix.Core.Extensions;

public static class TypeExtensions
{
	public static bool IsStructOrClass(this Type type)
	{
		if (type == typeof(string))
		{
			return false;
		}

		return (!type.IsPrimitive && type.IsValueType && !type.IsEnum) || type.IsClass || type.IsInterface;
	}
}
