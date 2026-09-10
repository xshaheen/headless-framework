// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>
/// Resolves a raw, caller-supplied tenant identifier into a <see cref="TenantResolutionOutcome"/>, and
/// loads <see cref="TenantInfo"/> by canonical id. HTTP-agnostic — consumed by the pre-auth identifier
/// resolution seam and by <see cref="ICurrentTenantInfo"/>. Owns normalization, shape validation,
/// ignored-identifier filtering, and read-through caching; stores never see raw caller input.
/// </summary>
[PublicAPI]
public interface ITenantCatalogService
{
    /// <summary>
    /// Resolves a raw tenant identifier: normalize → shape-validate → ignored-check → cache/store lookup.
    /// </summary>
    /// <param name="identifier">The raw, caller-supplied identifier (for example a hostname label).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Exactly one <see cref="TenantResolutionOutcome"/> per KTD4's classification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
    Task<TenantResolutionOutcome> ResolveAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads <see cref="TenantInfo"/> for a canonical tenant id through the same cache the identifier
    /// resolution path uses. Never rejects on a disabled tenant — rejection is a resolution-time concern
    /// only (R9).
    /// </summary>
    /// <param name="id">The canonical tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="TenantInfo"/>, or <see langword="null"/> when <paramref name="id"/> has no catalog row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="ITenantCatalogService"/> implementation, mirroring <c>SettingValueStore</c>'s service/SPI split.</summary>
internal sealed class TenantCatalogService(
    ITenantStore store,
    ICache<TenantIdentifierCacheItem> identifierCache,
    ICache<TenantInfoCacheItem> infoCache,
    IOptions<TenantCatalogOptions> options,
    TenantCatalogIgnoredIdentifierSet ignoredIdentifiers,
    ILogger<TenantCatalogService> logger
) : ITenantCatalogService
{
    private readonly TenantCatalogOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<TenantResolutionOutcome> ResolveAsync(
        string identifier,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(identifier);

        var normalized = identifier.Trim().ToLowerInvariant();

        if (
            normalized.Length == 0
            || normalized.Length > _options.MaxIdentifierLength
            || !_options.IdentifierPattern.IsMatch(normalized)
        )
        {
            return TenantResolutionOutcome.Invalid;
        }

        if (ignoredIdentifiers.Contains(normalized))
        {
            return TenantResolutionOutcome.Ignored;
        }

        var tenant = await _ResolveByIdentifierAsync(normalized, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return TenantResolutionOutcome.Unknown;
        }

        return tenant.IsEnabled ? TenantResolutionOutcome.Resolved(tenant) : TenantResolutionOutcome.Disabled;
    }

    /// <inheritdoc/>
    public async Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(id);

        return await _ResolveByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the identifier→id axis, then delegates to <see cref="_ResolveByIdAsync"/> for the id→info
    /// axis on a cache hit, or caches both axes from a single store hit on a miss. Two shapes implement that:
    /// a factory-backed read that adds per-key single-flight, and — when negative caching is disabled — a
    /// plain read-then-conditional-write.
    /// </summary>
    private Task<TenantInfo?> _ResolveByIdentifierAsync(
        string normalizedIdentifier,
        CancellationToken cancellationToken
    )
    {
        var identifierCacheKey = TenantIdentifierCacheItem.CalculateCacheKey(normalizedIdentifier);

        // `UnknownIdentifierCacheExpiration = 0` means "an unknown identifier never enters the cache" — the
        // control for attacker-influenced keyspace. A factory-backed read cannot honor it: GetOrAddAsync always
        // persists whatever the factory returns, and a zero duration is a write followed by an immediate
        // eviction (a delete round trip on a network cache), not a skipped write. Hosts that disable negative
        // caching therefore keep the read-then-conditional-write shape and trade single-flight away for it.
        return _options.UnknownIdentifierCacheExpiration > TimeSpan.Zero
            ? _GetOrAddByIdentifierAsync(normalizedIdentifier, identifierCacheKey, cancellationToken)
            : _ReadThroughByIdentifierAsync(normalizedIdentifier, identifierCacheKey, cancellationToken);
    }

    /// <summary>
    /// Factory-backed identifier resolution: one <c>GetOrAddAsync</c> call covers the read, the single-flight
    /// per-key lock (so a concurrent expiry rollover for one identifier costs one store read, not one per
    /// caller), and the write of the positive or negative entry under its own expiration.
    /// </summary>
    private async Task<TenantInfo?> _GetOrAddByIdentifierAsync(
        string normalizedIdentifier,
        string identifierCacheKey,
        CancellationToken cancellationToken
    )
    {
        // These two flags split the single exception surface of GetOrAddAsync into the three outcomes KTD4
        // distinguishes: a fault before the factory ran is a cache read fault (degrade to a miss), a fault after
        // the store answered is a cache write fault (swallow; the store-derived outcome stands), and a fault
        // between the two is the store's own (propagate unwrapped — never caught here).
        var factoryStarted = false;
        var storeAnswered = false;
        TenantInfo? freshFromStore = null;

        CacheValue<TenantIdentifierCacheItem> cached;

        try
        {
            cached = await identifierCache
                .GetOrAddAsync(
                    identifierCacheKey,
                    async (context, factoryCancellationToken) =>
                    {
                        factoryStarted = true;

                        var tenant = await store
                            .FindByIdentifierAsync(normalizedIdentifier, factoryCancellationToken)
                            .ConfigureAwait(false);

                        storeAnswered = true;
                        freshFromStore = tenant;

                        // Adaptive expiration: a negative entry lives under the shorter unknown-identifier window.
                        context.Options = CacheEntryOptions.FromTimeSpan(
                            tenant is null ? _options.UnknownIdentifierCacheExpiration : _options.CacheExpiration
                        );

                        if (tenant is null)
                        {
                            return context.Modified(new TenantIdentifierCacheItem(tenantId: null));
                        }

                        // One store hit still populates both axes: the identifier→id mapping written here, and
                        // the id→TenantInfo shape, so the caller's own chain into _ResolveByIdAsync — and a later
                        // FindByIdAsync for the same tenant — does not have to re-read the store.
                        await _CacheTenantInfoAsync(tenant, factoryCancellationToken).ConfigureAwait(false);

                        return context.Modified(new TenantIdentifierCacheItem(tenant.Id));
                    },
                    CacheEntryOptions.FromTimeSpan(_options.CacheExpiration),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The store already answered, so only the cache write (or its option validation) can have faulted: KTD4 keeps the store-derived outcome and issues no second store read. OperationCanceledException is excluded so caller cancellation still propagates.
        catch (Exception fault) when (fault is not OperationCanceledException && storeAnswered)
#pragma warning restore CA1031
        {
            logger.LogTenantCatalogCacheWriteFaulted(fault, nameof(TenantIdentifierCacheItem));

            return freshFromStore;
        }
#pragma warning disable CA1031 // The factory never ran, so the fault is on the cache read side: KTD4 degrades it to a miss and falls through to the store. OperationCanceledException is excluded so caller cancellation still propagates.
        catch (Exception fault) when (fault is not OperationCanceledException && !factoryStarted)
#pragma warning restore CA1031
        {
            logger.LogTenantCatalogCacheReadFaultedDegradingToMiss(fault, nameof(TenantIdentifierCacheItem));

            return await _LoadByIdentifierFromStoreAsync(normalizedIdentifier, identifierCacheKey, cancellationToken)
                .ConfigureAwait(false);
        }

        if (storeAnswered)
        {
            // The fresh store instance is returned directly (not re-read through the id axis), preserving
            // subclass identity for the typed accessor's downcast fast path (R10) while the cached copy stays
            // an isolated base-shape clone (R9's defensive-snapshot contract; KTD5).
            return freshFromStore;
        }

        if (!cached.HasValue)
        {
            // A lock-timeout degradation returns a miss without ever running the factory; treat it as a miss.
            return await _LoadByIdentifierFromStoreAsync(normalizedIdentifier, identifierCacheKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var cachedId = cached.Value?.TenantId;

        return cachedId is null ? null : await _ResolveByIdAsync(cachedId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Identifier resolution for hosts that disabled negative caching: read the cache, and write only when the
    /// store actually found a tenant. No single-flight — see <see cref="_ResolveByIdentifierAsync"/>.
    /// </summary>
    private async Task<TenantInfo?> _ReadThroughByIdentifierAsync(
        string normalizedIdentifier,
        string identifierCacheKey,
        CancellationToken cancellationToken
    )
    {
        var cachedIdentifier = await _TryGetCacheAsync(identifierCache, identifierCacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (cachedIdentifier.HasValue)
        {
            var cachedId = cachedIdentifier.Value?.TenantId;

            return cachedId is null ? null : await _ResolveByIdAsync(cachedId, cancellationToken).ConfigureAwait(false);
        }

        return await _LoadByIdentifierFromStoreAsync(normalizedIdentifier, identifierCacheKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the identifier from the store and best-effort populates both cache axes. Shared by the
    /// negative-caching-disabled path and by the cache-read-fault fallback, so both keep the write rules the
    /// factory-backed path applies: a negative entry only when negative caching is enabled, and the id axis
    /// written from the same store hit.
    /// </summary>
    private async Task<TenantInfo?> _LoadByIdentifierFromStoreAsync(
        string normalizedIdentifier,
        string identifierCacheKey,
        CancellationToken cancellationToken
    )
    {
        var tenant = await store.FindByIdentifierAsync(normalizedIdentifier, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            if (_options.UnknownIdentifierCacheExpiration > TimeSpan.Zero)
            {
                await _TryUpsertCacheAsync(
                        identifierCache,
                        identifierCacheKey,
                        new TenantIdentifierCacheItem(tenantId: null),
                        _options.UnknownIdentifierCacheExpiration,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            return null;
        }

        // Single store hit populates both axes: the identifier→id mapping, and the id→TenantInfo shape
        // (avoiding a second store round trip through _ResolveByIdAsync for the info we already have).
        await _TryUpsertCacheAsync(
                identifierCache,
                identifierCacheKey,
                new TenantIdentifierCacheItem(tenant.Id),
                _options.CacheExpiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        await _CacheTenantInfoAsync(tenant, cancellationToken).ConfigureAwait(false);

        // The fresh store instance is returned directly (not the clone written to cache), preserving
        // subclass identity for the typed accessor's downcast fast path (R10) while the cached copy stays
        // an isolated base-shape clone (R9's defensive-snapshot contract; KTD5).
        return tenant;
    }

    /// <summary>
    /// Resolves the id→<see cref="TenantInfo"/> axis: cache hit returns a defensive base-shape clone;
    /// cache miss consults the store directly and caches a base-shape clone for next time. No negative
    /// caching on this axis — which is also why this axis keeps the read-then-conditional-write shape
    /// instead of <c>GetOrAddAsync</c>: a factory-backed read cannot express "do not cache this result",
    /// and caching an id that has no catalog row would change <see cref="FindByIdAsync"/>'s contract.
    /// </summary>
    private async Task<TenantInfo?> _ResolveByIdAsync(string id, CancellationToken cancellationToken)
    {
        var idCacheKey = TenantInfoCacheItem.CalculateCacheKey(id);
        var cached = await _TryGetCacheAsync(infoCache, idCacheKey, cancellationToken).ConfigureAwait(false);

        if (cached.HasValue)
        {
            // Cache always holds the base shape only (R13) — always clone before handing it out so a
            // caller mutating ExtraProperties cannot corrupt the shared cached instance (R9, KTD5).
            return cached.Value is null ? null : _CloneToBaseShape(cached.Value.TenantInfo);
        }

        var tenant = await store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return null;
        }

        await _CacheTenantInfoAsync(tenant, cancellationToken).ConfigureAwait(false);

        // Fresh store instance, exclusively owned by this call — safe to return without cloning, and
        // preserves subclass identity for the typed accessor's downcast fast path.
        return tenant;
    }

    private Task _CacheTenantInfoAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        var idCacheKey = TenantInfoCacheItem.CalculateCacheKey(tenant.Id);
        var baseShape = _CloneToBaseShape(tenant);

        return _TryUpsertCacheAsync(
            infoCache,
            idCacheKey,
            new TenantInfoCacheItem(baseShape),
            _options.CacheExpiration,
            cancellationToken
        );
    }

    private static TenantInfo _CloneToBaseShape(TenantInfo source)
    {
        return new TenantInfo(source.Id, source.Identifier, source.Name, source.IsEnabled)
        {
            ExtraProperties = new ExtraProperties(source.ExtraProperties),
        };
    }

    /// <summary>
    /// Reads from <paramref name="cache"/>, degrading a read fault to a miss (KTD4) so the caller falls
    /// through to the store. <see cref="OperationCanceledException"/> is never a cache fault and always
    /// propagates unchanged.
    /// </summary>
    private async Task<CacheValue<T>> _TryGetCacheAsync<T>(
        ICache<T> cache,
        string cacheKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cache read faults degrade to a miss by design (KTD4); the store is the source of truth and is consulted next. OperationCanceledException is excluded so caller cancellation still propagates.
        catch (Exception fault) when (fault is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogTenantCatalogCacheReadFaultedDegradingToMiss(fault, typeof(T).Name);

            return CacheValue<T>.NoValue;
        }
    }

    /// <summary>
    /// Writes to <paramref name="cache"/>, swallowing a write fault (KTD4) so the outcome already derived
    /// from the store stays unchanged. <see cref="OperationCanceledException"/> always propagates unchanged.
    /// </summary>
    private async Task _TryUpsertCacheAsync<T>(
        ICache<T> cache,
        string cacheKey,
        T value,
        TimeSpan expiration,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await cache.UpsertAsync(cacheKey, value, expiration, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Cache write faults must never surface to the caller (KTD4): the store-derived outcome already computed is authoritative regardless of whether the cache write below succeeds. OperationCanceledException is excluded so caller cancellation still propagates.
        catch (Exception fault) when (fault is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogTenantCatalogCacheWriteFaulted(fault, typeof(T).Name);
        }
    }
}

internal static partial class TenantCatalogServiceLogger
{
    [LoggerMessage(
        EventId = 10,
        EventName = "TenantCatalogCacheReadFaultedDegradingToMiss",
        Level = LogLevel.Warning,
        Message = "Tenant catalog cache read of {CacheItemType} faulted; degrading to a cache miss and falling through to the store."
    )]
    public static partial void LogTenantCatalogCacheReadFaultedDegradingToMiss(
        this ILogger logger,
        Exception exception,
        string cacheItemType
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "TenantCatalogCacheWriteFaulted",
        Level = LogLevel.Warning,
        Message = "Tenant catalog cache write of {CacheItemType} faulted; the resolved outcome is unaffected."
    )]
    public static partial void LogTenantCatalogCacheWriteFaulted(
        this ILogger logger,
        Exception exception,
        string cacheItemType
    );
}
