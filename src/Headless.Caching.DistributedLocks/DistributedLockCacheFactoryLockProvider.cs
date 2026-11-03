// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.DistributedLocks;

namespace Headless.Caching;

/// <summary>
/// Bridges the caching factory-lock seam (<see cref="ICacheFactoryLockProvider"/>) onto
/// <see cref="IDistributedLock"/>: the cache instance name and key, namespaced with
/// <see cref="CacheFactoryLockOptions.ResourcePrefix"/>, form the lock resource (hashed when long), the seam timeout maps
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

    // Cache keys are unbounded, but every lock provider rejects a resource name over its configured maximum
    // (1024 by default) and SQL Server's native limit is 255. Past this length the instance-qualified key is replaced
    // by its SHA-256 digest, so the resource stays within every provider's default bound and is the same on every node.
    internal const int MaxReadableResourceLength = 255;

    private const string _HashMarker = "sha256:";

    // The digest form (prefix + marker + 64 hex chars) must itself fit the readable bound.
    internal const int MaxResourcePrefixLength = MaxReadableResourceLength - 7 - 64;

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(
        string cacheName,
        string key,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(cacheName);
        Argument.IsNotNullOrEmpty(key);

        var acquireOptions = new DistributedLockAcquireOptions
        {
            AcquireTimeout = timeout,
            TimeUntilExpires = _options.TimeUntilExpires,
        };

        return await _distributedLock
            .TryAcquireAsync(BuildResource(_options.ResourcePrefix, cacheName, key), acquireOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string BuildResource(string resourcePrefix, string cacheName, string key)
    {
        // The instance name keeps two caches that hold the same key on separate locks: they store separate entries,
        // and a factory of one cache that reads the other under the same key would otherwise wait on its own lock.
        var qualified = $"{cacheName}:{key}";

        if (resourcePrefix.Length + qualified.Length <= MaxReadableResourceLength)
        {
            return resourcePrefix + qualified;
        }

        return $"{resourcePrefix}{_HashMarker}{qualified.ToSha256()}";
    }
}
