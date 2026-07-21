// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.DashboardDtos;

/// <summary>Dashboard projection of a single node's liveness, sourced from the coordination membership substrate.</summary>
[PublicAPI]
public sealed class LiveNodeView
{
    /// <summary>Incarnation-qualified identity in the canonical <c>node@incarnation</c> form.</summary>
    public required string Identity { get; init; }

    /// <summary>Liveness state name: <c>Alive</c>, <c>Suspected</c>, or <c>Dead</c>.</summary>
    public required string State { get; init; }

    /// <summary>Optional role advertised by the node, if any.</summary>
    public string? Role { get; init; }

    /// <summary>Best-effort last-heartbeat timestamp surfaced from node metadata, if the provider supplies one.</summary>
    public string? LastBeat { get; init; }

    /// <summary>Raw node metadata as advertised by the provider.</summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}
