// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Keys a tenant-routed context's model by its effective schema, so each schema gets its own model (and, through
/// the model, its own compiled queries) while contexts of one schema share one.
/// </summary>
/// <remarks>
/// The models live in the routed type's own size-bounded memory cache (see <see cref="HeadlessTenantDataRouting"/>);
/// EF's default cache would evict and rebuild them past about 40 schemas.
/// </remarks>
internal sealed class HeadlessTenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var schema = context is HeadlessDbContext { RoutedPlacement: { } placement } ? placement.EffectiveSchema : null;

        // The model cache is shared process-wide, so the key also carries a singleton of the context's EF internal
        // service provider: two hosts that route one context type through different providers or model-affecting
        // options have different internal providers and must never share a model, as EF's own per-provider cache
        // guarantees.
        return new TenantModelCacheKey(context.GetType(), schema, designTime, context.GetService<IModelSource>());
    }

    private readonly record struct TenantModelCacheKey(
        Type ContextType,
        string? Schema,
        bool DesignTime,
        IModelSource ServiceProviderIdentity
    );
}
