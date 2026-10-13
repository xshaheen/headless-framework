// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Bounds the function tag on Jobs metrics to the functions one host registered. A function name read from a stored
/// row is outside the host's control (a row written by a newer build, or for a removed function), so an unregistered
/// name is tagged <see cref="Other" /> instead of growing the metric's series without limit.
/// </summary>
/// <remarks>
/// One instance per service provider, built from that host's frozen registry: the meter is process-wide, so two hosts
/// in one process must not share the set of names each lets through.
/// </remarks>
internal sealed class JobsFunctionTagBound(JobFunctionRegistry? registry)
{
    /// <summary>The tag value for a function this host does not register.</summary>
    internal const string Other = "_other";

    /// <summary>A bound for a host without a registry: every name is tagged <see cref="Other" />.</summary>
    internal static readonly JobsFunctionTagBound Unregistered = new(registry: null);

    /// <summary>Returns <paramref name="function" /> when this host registers it; otherwise <see cref="Other" />.</summary>
    public string Tag(string? function)
    {
        if (
            function is null
            || registry is null
            || (!registry.Descriptors.ContainsKey(function) && !registry.Functions.ContainsKey(function))
        )
        {
            return Other;
        }

        return function;
    }
}
