// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.DistributedLocks;

/// <summary>
/// Per-resource waiter accounting for DoS protection. Tracks how many acquirers are currently blocked
/// waiting for each contended resource and enforces the configured caps
/// (<c>MaxConcurrentWaitingResources</c> / <c>MaxWaitersPerResource</c>).
/// </summary>
/// <remarks>
/// This isolates only the cap-counting concern so multiple providers can share identical enforcement
/// without duplicating the count map and the cap checks. Wake-ups on release live in
/// <see cref="ReleaseWaiterRegistry"/>; a provider that caps waiters enters this registry first, so a
/// refused waiter never registers for wake-ups.
/// All access is serialized on an internal lock; <see cref="Enter"/>/<see cref="Exit"/> must be paired.
/// </remarks>
internal sealed class WaiterCapRegistry(int? maxConcurrentWaitingResources, int? maxWaitersPerResource)
{
    private readonly Dictionary<string, int> _waitersByResource = [with(StringComparer.Ordinal)];
    private readonly Lock _gate = new();

    /// <summary>
    /// Accounts for one more waiter on <paramref name="resource"/>, throwing
    /// <see cref="InvalidOperationException"/> when a configured cap would be exceeded.
    /// </summary>
    public void Enter(string resource)
    {
        lock (_gate)
        {
            if (_waitersByResource.TryGetValue(resource, out var existing))
            {
                if (maxWaitersPerResource is { } maxPerResource)
                {
                    Ensure.True(existing < maxPerResource, $"Maximum waiters per resource ({maxPerResource}) exceeded");
                }

                _waitersByResource[resource] = existing + 1;

                return;
            }

            if (maxConcurrentWaitingResources is { } maxResources)
            {
                Ensure.True(
                    _waitersByResource.Count < maxResources,
                    $"Maximum concurrent waiting resources ({maxResources}) exceeded"
                );
            }

            _waitersByResource[resource] = 1;
        }
    }

    /// <summary>Releases one waiter slot on <paramref name="resource"/>; the inverse of <see cref="Enter"/>.</summary>
    public void Exit(string resource)
    {
        lock (_gate)
        {
            if (!_waitersByResource.TryGetValue(resource, out var existing))
            {
                return;
            }

            if (existing <= 1)
            {
                _waitersByResource.Remove(resource);
            }
            else
            {
                _waitersByResource[resource] = existing - 1;
            }
        }
    }
}
