// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Coordinates factory-backed cache operations across cache providers.</summary>
[PublicAPI]
public sealed class FactoryCacheCoordinator(TimeProvider timeProvider, ILogger? logger = null)
    : IDisposable
{
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
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

        if (options.IsFailSafeEnabled)
        {
            Argument.IsPositive(options.FailSafeMaxDuration);
            Argument.IsPositive(options.FailSafeThrottleDuration);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
        var now = _GetUtcNow();

        if (entry.IsFresh(now))
        {
            return _ToCacheValue(entry, isStale: false);
        }

        var staleCandidate = _IsStaleCandidate(entry, now) ? entry : CacheStoreEntry<T>.NotFound;

        using (await _keyedLock.LockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            entry = await _TryGetEntryAsync<T>(store, key, cancellationToken).ConfigureAwait(false);
            now = _GetUtcNow();

            if (entry.IsFresh(now))
            {
                return _ToCacheValue(entry, isStale: false);
            }

            if (_IsStaleCandidate(entry, now))
            {
                staleCandidate = entry;
            }

            T? value;

            try
            {
                value = await factory(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
            {
                now = _GetUtcNow();

                if (!options.IsFailSafeEnabled || !_IsStaleCandidate(staleCandidate, now))
                {
                    throw;
                }

                await _TryRestampStaleAsync(store, key, staleCandidate, options, now).ConfigureAwait(false);

                _logger.LogFailSafeActivated(key, exception.GetType().Name);
                return _ToCacheValue(staleCandidate, isStale: true);
            }

            // The factory succeeded: persist the fresh value and return it. A store-write failure on the
            // fresh path must propagate rather than activate fail-safe (which would discard the fresh value).
            now = _GetUtcNow();
            var logicalExpiresAt = now.Add(options.Duration);
            var physicalDuration = options.IsFailSafeEnabled
                ? _Max(options.Duration, options.FailSafeMaxDuration)
                : options.Duration;
            var physicalExpiresAt = now.Add(physicalDuration);

            await store
                .SetEntryAsync(key, value, isNull: value is null, logicalExpiresAt, physicalExpiresAt, cancellationToken)
                .ConfigureAwait(false);

            return new CacheValue<T>(value, hasValue: true);
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
        DateTime now
    )
    {
        // staleCandidate always carries a physical expiration: _IsStaleCandidate (the only gate that assigns a
        // stale candidate) requires PhysicalExpiresAt.HasValue, so the throttle restamp can always be written.
        var physicalExpiresAt = staleCandidate.PhysicalExpiresAt!.Value;
        var logicalExpiresAt = _Min(now.Add(options.FailSafeThrottleDuration), physicalExpiresAt);

        try
        {
            // The restamp is a throttle optimization, not caller work, so it uses CancellationToken.None: a
            // caller cancellation between the factory throw and this await must not abort the stale return.
            await store
                .SetEntryAsync(
                    key,
                    staleCandidate.Value,
                    staleCandidate.IsNull,
                    logicalExpiresAt,
                    physicalExpiresAt,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Swallow all exceptions (including cancellation): the stale value must always be returned.
            _logger.LogFailSafeRestampFailed(exception, key);
        }
    }

    private async ValueTask<CacheStoreEntry<T>> _TryGetEntryAsync<T>(
        IFactoryCacheStore store,
        string key,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await store.TryGetEntryAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogCacheStoreReadFailed(exception, key);
            return CacheStoreEntry<T>.NotFound;
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

    // A fail-safe stale candidate must carry a non-null physical expiration. A genuine fail-safe reserve
    // always has one (the coordinator writes it); requiring it here closes the throttle hole where a
    // null-physical entry would be served as stale without a throttle write, hammering the factory.
    private static bool _IsStaleCandidate<T>(CacheStoreEntry<T> entry, DateTime now) =>
        entry.IsPhysicallyPresent(now) && entry.PhysicalExpiresAt.HasValue;

    // Caller cancellation (the caller's own token) must always propagate and never activate fail-safe (KTD-7).
    // Use token identity, not just IsCancellationRequested, so an OperationCanceledException raised by an
    // unrelated linked/internal token (e.g. a downstream timeout) still activates fail-safe.
    /// <summary>
    /// Returns whether <paramref name="exception"/> represents cancellation of the caller's own token, which must
    /// always propagate rather than activate fail-safe (KTD-7). An <see cref="OperationCanceledException"/> raised
    /// by an unrelated linked/internal token (for example a downstream timeout) is NOT caller cancellation and
    /// should activate fail-safe / degrade to a miss. Providers composing this engine (e.g. a hybrid store)
    /// should use this predicate for their best-effort catch filters so cancellation semantics stay consistent.
    /// </summary>
    /// <param name="exception">The exception thrown by the factory or store operation.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    [Pure]
    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        // Identity-match only when the caller supplied a cancellable token. CancellationToken.None compares equal
        // to a token-less OperationCanceledException's token (default == default), which would otherwise wrongly
        // suppress fail-safe for a downstream OCE when the caller passed no token (the default-token call shape).
        return cancellationToken.CanBeCanceled
            && exception is OperationCanceledException operationCanceled
            && operationCanceled.CancellationToken == cancellationToken;
    }

    private DateTime _GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

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
        Level = LogLevel.Warning,
        Message = "Cache fail-safe restamp failed for key {Key}; stale value will still be returned, but the "
            + "throttle window was not persisted so the factory may be retried sooner than expected."
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
