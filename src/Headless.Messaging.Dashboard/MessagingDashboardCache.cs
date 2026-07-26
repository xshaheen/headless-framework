// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Caching.Memory;

namespace Headless.Messaging.Dashboard;

/// <summary>Short-lived in-process memoization owned by the messaging dashboard.</summary>
/// <remarks>
/// The dashboard deliberately does not call <c>AddMemoryCache()</c>. That registers the shared
/// <see cref="IMemoryCache"/> into the host's container, so dashboard entries would land in the application's
/// cache — competing for its size limit and evictable by its compaction — purely as a side effect of adding a
/// diagnostics UI. This instance is created, sized, and disposed by the dashboard alone.
/// <para>
/// It is a concrete type rather than <c>Headless.Caching.ICache</c> on purpose: <c>AddHeadlessCaching</c>
/// accepts only one call per service collection, so registering a Headless cache from here would either throw
/// for every consumer that configures caching themselves or force them to adopt the caching package to use the
/// dashboard. The cached values are per-node discovery results and metrics snapshots that must never leave the
/// process anyway.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class MessagingDashboardCache : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    /// <summary>Reads <paramref name="key"/>, returning <see langword="false"/> when absent or expired.</summary>
    public bool TryGetValue<T>(string key, out T? value) => _cache.TryGetValue(key, out value);

    /// <summary>Writes <paramref name="value"/> so it expires <paramref name="lifetime"/> from now.</summary>
    public void Set<T>(string key, T value, TimeSpan lifetime) => _cache.Set(key, value, lifetime);

    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();
}
