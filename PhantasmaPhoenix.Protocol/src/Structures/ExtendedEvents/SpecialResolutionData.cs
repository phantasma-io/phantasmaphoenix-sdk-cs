using System;
using System.Collections.Generic;

namespace PhantasmaPhoenix.Protocol.ExtendedEvents;

public struct SpecialResolutionCall
{
    public uint ModuleId;
    public string Module = string.Empty;
    public uint MethodId;
    public string Method = string.Empty;
    public Dictionary<string, string>? Arguments;
    public SpecialResolutionCall[]? Calls;

    public SpecialResolutionCall(
        uint moduleId,
        string module,
        uint methodId,
        string method,
        Dictionary<string, string>? arguments = null,
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
