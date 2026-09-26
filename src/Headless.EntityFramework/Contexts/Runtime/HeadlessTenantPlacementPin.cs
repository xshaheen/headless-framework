// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// EF's default internal cache (10240 units) keeps about 40 schemas before it evicts and rebuilds models: EF Core 10
/// caches a design-time model (150) and a runtime model (100) per schema, sharing the budget with compiled queries
/// (10 each). Each routed type therefore gets its own cache sized for its schema count plus a compiled-query
/// allowance per schema. One instance per type keeps every routed instance of that type on one EF service provider.
/// </remarks>
internal sealed class HeadlessTenantDataRouting : IDisposable
{
    private const long _ModelUnitsPerSchema = 250;
    private const long _CompiledQueryUnitsPerSchema = 1000;

    private readonly FrozenDictionary<Type, MemoryCache> _caches;

    public HeadlessTenantDataRouting(IEnumerable<HeadlessTenantRoutedContext> routedContexts)
    {
        _caches = routedContexts.ToFrozenDictionary(
            static routed => routed.ContextType,
            static routed => new MemoryCache(
                new MemoryCacheOptions
                {
                    SizeLimit = routed.MaxCachedSchemas * (_ModelUnitsPerSchema + _CompiledQueryUnitsPerSchema),
                }
            )
        );
    }

    public bool IsRouted(Type contextType) => _caches.ContainsKey(contextType);

    public IMemoryCache GetModelCache(Type contextType) => _caches[contextType];

    public void Dispose()
    {
        foreach (var cache in _caches.Values)
        {
            cache.Dispose();
        }
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
