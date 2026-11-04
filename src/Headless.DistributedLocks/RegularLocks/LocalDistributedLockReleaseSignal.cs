// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// The default <see cref="IDistributedLockReleaseSignal"/>: hands each release straight to this process's
/// <see cref="IDistributedLockReleaseListener"/>s, so a waiter in the same process wakes at once. Waiters on other hosts
/// still wake on their backoff poll until a cross-host transport replaces it.
/// </summary>
/// <remarks>
/// The listeners are resolved on the first release, not in the constructor: they are the lock providers, which depend
/// on this signal.
/// </remarks>
internal sealed class LocalDistributedLockReleaseSignal : IDistributedLockReleaseSignal
{
    private readonly IServiceProvider _services;
    private IDistributedLockReleaseListener[]? _listeners;

    /// <summary>Creates the signal, logging once when <paramref name="logger"/> is given.</summary>
    /// <param name="services">Resolves the release listeners on the first release.</param>
    /// <param name="logger">
    /// Logs once that releases do not reach other hosts; <see langword="null"/> for a provider that only ever runs in
    /// one process, where there are no other hosts to reach.
    /// </param>
    public LocalDistributedLockReleaseSignal(IServiceProvider services, ILogger? logger)
    {
        _services = services;
        logger?.LogCrossHostReleaseSignalAbsent();
    }

    public void Announce(string resource, string leaseId)
    {
        // A race that resolves twice builds the same array from the same singletons, so no lock is needed.
        var listeners = _listeners ??= [.. _services.GetServices<IDistributedLockReleaseListener>()];

        foreach (var listener in listeners)
        {
            listener.OnReleased(resource, leaseId);
        }
    }
}
