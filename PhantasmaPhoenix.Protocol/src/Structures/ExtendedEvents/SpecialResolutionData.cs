using System;
using Newtonsoft.Json;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

/// <summary>
/// One call carried by a special resolution.
/// </summary>
/// <remarks>
/// <see cref="Arguments"/> is typed per method: the converter picks the concrete
/// <see cref="SpecialResolutionArguments"/> from <see cref="Module"/> and <see cref="Method"/>, and
/// falls back to <see cref="RawArguments"/> for a method the answering node did not decode.
/// </remarks>
[JsonConverter(typeof(SpecialResolutionCallJsonConverter))]
public struct SpecialResolutionCall
{
	public uint ModuleId;
	public string Module = string.Empty;
	public uint MethodId;
	public string Method = string.Empty;
	public SpecialResolutionArguments? Arguments;
	public SpecialResolutionCall[]? Calls;

	public SpecialResolutionCall(
		uint moduleId,
		string module,
		uint methodId,
		string method,
		SpecialResolutionArguments? arguments = null,
		SpecialResolutionCall[]? calls = null)
	{
		ModuleId = moduleId;
		Module = module;
		MethodId = methodId;
		Method = method;
		Arguments = arguments;
		Calls = calls;
	}
}

public struct SpecialResolutionData
{
	public ulong ResolutionId;
	public string? Description;
	public SpecialResolutionCall[] Calls = Array.Empty<SpecialResolutionCall>();

	public SpecialResolutionData(ulong resolutionId, string? description, SpecialResolutionCall[]? calls = null)
	{
		ResolutionId = resolutionId;
		Description = description;
		Calls = calls ?? Array.Empty<SpecialResolutionCall>();
	}
}
