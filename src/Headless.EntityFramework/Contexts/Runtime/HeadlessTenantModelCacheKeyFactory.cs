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

        return new TenantModelCacheKey(context.GetType(), schema, designTime);
    }

    private readonly record struct TenantModelCacheKey(Type ContextType, string? Schema, bool DesignTime);
}
