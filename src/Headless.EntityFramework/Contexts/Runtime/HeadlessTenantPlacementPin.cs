// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using Headless.MultiTenancy;
using Microsoft.Extensions.Caching.Memory;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>The routing options recorded for one tenant-routed context type.</summary>
internal sealed record HeadlessTenantRoutedContext(Type ContextType, int MaxCachedSchemas);

/// <summary>
/// The tenant-routed context types and, per type, the bounded memory cache that holds their per-schema models.
/// </summary>
/// <remarks>
/// <para>
/// EF's default internal cache (10240 units) keeps about 40 schemas before it evicts and rebuilds models: EF Core 10
/// caches a design-time model (150) and a runtime model (100) per schema, sharing the budget with compiled queries
/// (10 each). Each routed type therefore gets its own cache sized for its schema count plus a compiled-query
/// allowance per schema.
/// </para>
/// <para>
/// The caches are process-wide, keyed by context type and size, like EF's own model cache. A new cache instance
/// forces a new EF internal service provider, and EF refuses to build more than twenty per process, so a cache per
/// application container would break any process that builds several hosts (integration test suites do).
/// </para>
/// </remarks>
internal sealed class HeadlessTenantDataRouting(IEnumerable<HeadlessTenantRoutedContext> routedContexts)
{
    private const long _ModelUnitsPerSchema = 250;
    private const long _CompiledQueryUnitsPerSchema = 1000;

    private static readonly ConcurrentDictionary<(Type ContextType, int MaxCachedSchemas), MemoryCache> _Caches = new();

    private readonly FrozenDictionary<Type, int> _routed = routedContexts.ToFrozenDictionary(
        static routed => routed.ContextType,
        static routed => routed.MaxCachedSchemas
    );

    public bool IsRouted(Type contextType) => _routed.ContainsKey(contextType);

    public IMemoryCache GetModelCache(Type contextType) =>
        _Caches.GetOrAdd(
            (contextType, _routed[contextType]),
            static key => new MemoryCache(
                new MemoryCacheOptions
                {
                    SizeLimit = key.MaxCachedSchemas * (_ModelUnitsPerSchema + _CompiledQueryUnitsPerSchema),
                }
            )
        );
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
