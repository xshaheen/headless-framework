// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Coordinates factory-backed cache operations across cache providers.</summary>
public sealed class FactoryCacheCoordinator(TimeProvider timeProvider, ILogger<FactoryCacheCoordinator>? logger = null)
    : IDisposable
{
    private readonly KeyedAsyncLock _keyedLock = new();
    private readonly ILogger _logger = logger ?? NullLogger<FactoryCacheCoordinator>.Instance;

    /// <summary>Gets or creates a cache value by using the provider store primitive.</summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The value factory.</param>
    /// <param name="options">The cache entry options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(store);
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        Argument.IsPositive(options.Duration);
        Argument.IsPositive(options.FailSafeMaxDuration);
        Argument.IsPositive(options.FailSafeThrottleDuration);
        cancellationToken.ThrowIfCancellationRequested();

        var read = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
        var now = _GetUtcNow();

        if (_IsFresh(read.Entry, now))
        {
            return _ToCacheValue(read.Entry, isStale: false);
        }

        var staleCandidate = _IsPhysicallyPresent(read.Entry, now) ? read.Entry : CacheStoreEntry<T>.NotFound;

        using (await _keyedLock.LockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            read = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
            now = _GetUtcNow();

            if (_IsFresh(read.Entry, now))
            {
                return _ToCacheValue(read.Entry, isStale: false);
            }

            if (_IsPhysicallyPresent(read.Entry, now))
            {
                staleCandidate = read.Entry;
            }

            try
            {
                var value = await factory(cancellationToken).ConfigureAwait(false);
                var logicalExpiresAt = now.Add(options.Duration);
                var physicalDuration = options.IsFailSafeEnabled
                    ? _Max(options.Duration, options.FailSafeMaxDuration)
                    : options.Duration;
                var physicalExpiresAt = now.Add(physicalDuration);

                await store
                    .SetEntryAsync(
                        key,
                        value,
                        isNull: value is null,
                        logicalExpiresAt,
                        physicalExpiresAt,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                return new CacheValue<T>(value, hasValue: true);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                now = _GetUtcNow();

                if (!options.IsFailSafeEnabled || !_IsPhysicallyPresent(staleCandidate, now))
                {
                    throw;
                }

                await _TryRestampStaleAsync(store, key, staleCandidate, options, now, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogFailSafeActivated(key, exception.GetType().Name);
                return _ToCacheValue(staleCandidate, isStale: true);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _keyedLock.Dispose();
    }

    private async ValueTask _TryRestampStaleAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheStoreEntry<T> staleCandidate,
        CacheEntryOptions options,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        if (!staleCandidate.PhysicalExpiresAt.HasValue)
        {
            return;
        }

        var logicalExpiresAt = _Min(now.Add(options.FailSafeThrottleDuration), staleCandidate.PhysicalExpiresAt.Value);

        try
        {
            await store
                .SetEntryAsync(
                    key,
                    staleCandidate.Value,
                    staleCandidate.IsNull,
                    logicalExpiresAt,
                    staleCandidate.PhysicalExpiresAt.Value,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogFailSafeRestampFailed(exception, key);
        }
    }

    private async ValueTask<(bool Succeeded, CacheStoreEntry<T> Entry)> _TryGetEntryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return (true, await store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogCacheStoreReadFailed(exception, key);
            return (false, CacheStoreEntry<T>.NotFound);
        }
    }

    private static CacheValue<T> _ToCacheValue<T>(CacheStoreEntry<T> entry, bool isStale)
    {
        if (!entry.Found)
        {
            return CacheValue<T>.NoValue;
        }

        return entry.IsNull
            ? new CacheValue<T>(default, hasValue: true, isStale)
            : new CacheValue<T>(entry.Value, hasValue: true, isStale);
    }

    private static bool _IsFresh<T>(CacheStoreEntry<T> entry, DateTime now)
    {
        if (!_IsPhysicallyPresent(entry, now))
        {
            return false;
        }

        return !entry.LogicalExpiresAt.HasValue || entry.LogicalExpiresAt.Value > now;
    }

    private static bool _IsPhysicallyPresent<T>(CacheStoreEntry<T> entry, DateTime now) =>
        entry.Found && (!entry.PhysicalExpiresAt.HasValue || entry.PhysicalExpiresAt.Value > now);

    private DateTime _GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static TimeSpan _Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static DateTime _Min(DateTime left, DateTime right) => left <= right ? left : right;
}

internal static partial class FactoryCacheCoordinatorLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "CacheFailSafeActivated",
        Level = LogLevel.Warning,
        Message = "Cache fail-safe activated for key {Key}; serving stale value after factory exception {ExceptionType}."
    )]
    public static partial void LogFailSafeActivated(this ILogger logger, string key, string exceptionType);

    [LoggerMessage(
        EventId = 2,
        EventName = "CacheFailSafeRestampFailed",
        Level = LogLevel.Debug,
        Message = "Cache fail-safe restamp failed for key {Key}; stale value will still be returned."
    )]
    public static partial void LogFailSafeRestampFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 3,
        EventName = "CacheStoreReadFailed",
        Level = LogLevel.Debug,
        Message = "Cache store read failed for key {Key}; treating it as a cache miss."
    )]
    public static partial void LogCacheStoreReadFailed(this ILogger logger, Exception exception, string key);
}
