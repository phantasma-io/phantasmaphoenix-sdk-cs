using Swashbuckle.AspNetCore.Annotations;

namespace PhantasmaPhoenix.RPC.Annotations;

public class ApiDescriptionAttribute : Attribute
{
	public readonly string Description;

	public ApiDescriptionAttribute(string description)
	{
		Description = description;
	}
}

public class ApiParameterAttribute : SwaggerParameterAttribute
{
	public readonly string? Value;

	public ApiParameterAttribute(string description, string value) : base(description)
	{
		Value = value;
	}

	public ApiParameterAttribute(string description) : base(description)
	{
		Value = null;
	}
}

public class ApiInfoAttribute : ApiDescriptionAttribute
{
	public readonly Type ReturnType;
	public readonly bool Paginated;
	public readonly int CacheDuration;
	public readonly string? CacheTag;

	public ApiInfoAttribute(Type returnType, string description, bool paginated = false, int cacheDuration = 0,
		string? cacheTag = null) : base(description)
	{
		ReturnType = returnType;
		Paginated = paginated;
		CacheDuration = cacheDuration;
		CacheTag = cacheTag;
	}
}
