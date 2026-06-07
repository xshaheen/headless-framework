// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Cold descriptor written when a node incarnation joins a coordination cluster.</summary>
[PublicAPI]
public sealed record NodeDescriptor
{
    public required NodeIdentity Identity { get; init; }

    public string? HostName { get; init; }

    public IReadOnlyDictionary<string, string> Endpoints { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string? Role { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
