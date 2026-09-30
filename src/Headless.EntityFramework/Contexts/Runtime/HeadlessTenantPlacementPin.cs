// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using Headless.MultiTenancy;
using Microsoft.Extensions.Caching.Memory;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>The routing options recorded for one tenant-routed context type.</summary>
internal sealed record HeadlessTenantRoutedContext(Type ContextType, int MaxCachedSchemas, string DataStore);

/// <summary>
/// The tenant-routed context types of a host and the one bounded memory cache that holds all their per-schema
/// models and compiled queries.
/// </summary>
/// <remarks>
/// <para>
/// EF's default internal cache (10240 units) keeps about 40 schemas before it evicts and rebuilds models: EF Core 10
/// caches a design-time model (150) and a runtime model (100) per schema, sharing the budget with compiled queries
/// (10 each). The routed types therefore get a cache sized for the sum of their schema budgets plus a compiled-query
/// allowance per schema; model cache keys carry the context type, so types never collide inside it.
/// </para>
/// <para>
/// One cache for every routed type of a host, rather than one per type, because a distinct cache instance forces a
/// distinct EF internal service provider, and EF throws past twenty per process: a cache per routed type would spend
/// one provider per context an application routes. The cache is process-wide, keyed by the routed set and its
/// budgets like EF's own model cache, so several hosts with the same routing share it (integration test suites
/// build many hosts in one process).
/// </para>
/// </remarks>
internal sealed class HeadlessTenantDataRouting
{
    private const long _ModelUnitsPerSchema = 250;
    private const long _CompiledQueryUnitsPerSchema = 1000;

    private static readonly ConcurrentDictionary<string, MemoryCache> _Caches = new(StringComparer.Ordinal);

    private readonly FrozenDictionary<Type, HeadlessTenantRoutedContext> _routed;
    private readonly MemoryCache _modelCache;

    public HeadlessTenantDataRouting(IEnumerable<HeadlessTenantRoutedContext> routedContexts)
    {
        _routed = routedContexts.ToFrozenDictionary(static routed => routed.ContextType);

        var entries = _routed
            .Values.Select(static routed =>
                (
                    Key: string.Create(
                        CultureInfo.InvariantCulture,
                        $"{routed.ContextType.AssemblyQualifiedName}|{routed.MaxCachedSchemas}"
                    ),
                    routed.MaxCachedSchemas
                )
            )
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        var cacheKey = string.Join('\n', entries.Select(static entry => entry.Key));
        var totalSchemas = entries.Sum(static entry => (long)entry.MaxCachedSchemas);

        _modelCache = _Caches.GetOrAdd(
            cacheKey,
            static (_, schemas) =>
                new MemoryCache(
                    new MemoryCacheOptions
                    {
                        SizeLimit = schemas * (_ModelUnitsPerSchema + _CompiledQueryUnitsPerSchema),
                    }
                ),
            totalSchemas
        );
    }

    public bool IsRouted(Type contextType) => _routed.ContainsKey(contextType);

    /// <summary>The placement request for <paramref name="tenantId"/> and the data store <paramref name="contextType"/> belongs to.</summary>
    public TenantDataPlacementRequest CreateRequest(Type contextType, string tenantId) =>
        new(tenantId, _routed[contextType].DataStore);

    public IMemoryCache GetModelCache(Type contextType)
    {
        // Indexing asserts the type is routed; every routed type of this host shares the one cache.
        _ = _routed[contextType];

        return _modelCache;
    }
}

/// <summary>
/// The placement a factory resolved for the context it is about to create. Scoped: the factory creates one scope
/// per context, so the pin reaches exactly that context. A context resolved straight from a request scope finds
/// it empty.
/// </summary>
internal sealed class HeadlessTenantPlacementPin
{
    public string? TenantId { get; private set; }

    public TenantDataPlacement? Placement { get; private set; }

    public void Set(string tenantId, TenantDataPlacement placement)
    {
        TenantId = tenantId;
        Placement = placement;
    }
}
