// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.Messaging.Internal;

/// <summary>
/// The sequential intake locks this host holds now, by lock resource, each with the lease id it holds it under.
/// </summary>
/// <remarks>
/// A lock store tells any host that a resource is held, and by which lease, but not by which host. The dashboard asks
/// each host whether it holds the resource and under which lease, so every node names the same holder.
/// </remarks>
internal sealed class SequentialIntakeHolds
{
    private readonly ConcurrentDictionary<string, string> _leases = new(StringComparer.Ordinal);

    /// <summary>Records that this host holds <paramref name="resource"/> under <paramref name="leaseId"/>.</summary>
    public void Hold(string resource, string leaseId)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        _leases[resource] = leaseId;
    }

    /// <summary>Forgets <paramref name="resource"/>, unless a later lease of it replaced <paramref name="leaseId"/>.</summary>
    public void Release(string resource, string leaseId)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        _leases.TryRemove(new KeyValuePair<string, string>(resource, leaseId));
    }

    /// <summary>The lease id this host holds <paramref name="resource"/> under, or <see langword="null"/>.</summary>
    public string? LeaseOf(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        return _leases.GetValueOrDefault(resource);
    }
}
