// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Selects individual unique indexes for tenant-scoped uniqueness.</summary>
[PublicAPI]
public static class HeadlessTenantIndexExtensions
{
    /// <summary>Appends the owning tenant property at model finalization, preserving subsequent index configuration.</summary>
    /// <remarks>The entity must be tenant-owned and the final index must be unique. Existing key columns retain their order.</remarks>
    public static IndexBuilder IsTenantScoped(this IndexBuilder builder)
    {
        builder.HasAnnotation(HeadlessTenantPolicyAnnotations.ScopedIndex, true);
        return builder;
    }

    /// <summary>Appends the owning tenant property at model finalization, preserving subsequent index configuration.</summary>
    /// <remarks>The entity must be tenant-owned and the final index must be unique. Existing key columns retain their order.</remarks>
    public static IndexBuilder<TEntity> IsTenantScoped<TEntity>(this IndexBuilder<TEntity> builder)
        where TEntity : class
    {
        IsTenantScoped((IndexBuilder)builder);
        return builder;
    }
}
