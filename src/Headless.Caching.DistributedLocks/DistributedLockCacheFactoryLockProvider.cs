// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;

namespace Headless.Caching;

/// <summary>
/// Bridges the caching factory-lock seam (<see cref="ICacheFactoryLockProvider"/>) onto
/// <see cref="IDistributedLock"/>: the cache key is namespaced with
/// <see cref="CacheFactoryLockOptions.ResourcePrefix"/> to form the lock resource, the seam timeout maps
/// directly onto <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> (<see cref="TimeSpan.Zero"/> is the
/// single try-once attempt, <see cref="Timeout.InfiniteTimeSpan"/> waits unboundedly), and the acquired
/// <see cref="IDistributedLease"/> is returned as the releaser.
/// </summary>
internal sealed class DistributedLockCacheFactoryLockProvider(
    IDistributedLock distributedLock,
    CacheFactoryLockOptions options
) : ICacheFactoryLockProvider
{
    private readonly IDistributedLock _distributedLock = Argument.IsNotNull(distributedLock);

    // Options are bound once at startup (snapshot value, matching the cache stack's AddSingletonOptionValue
    // convention) — the provider is a singleton and the factory-lock settings are not hot-reloadable. Switch the
    // injection to IOptionsMonitor<CacheFactoryLockOptions> if live reconfiguration is ever required.
    private readonly CacheFactoryLockOptions _options = Argument.IsNotNull(options);

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(key);

        var acquireOptions = new DistributedLockAcquireOptions
        {
            AcquireTimeout = timeout,
            TimeUntilExpires = _options.TimeUntilExpires,
        };

        return await _distributedLock
            .TryAcquireAsync(_options.ResourcePrefix + key, acquireOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
