// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Linq;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Provider-neutral helpers for composing entity query filters.</summary>
[PublicAPI]
public static class HeadlessEntityTypeBuilderExtensions
{
    /// <summary>
    /// It allows you to add a query filter to an entity type, combining it with
    /// any existing query filter if any using a logical AND operation
    /// it differs from <see cref="EntityTypeBuilder{TEntity}.HasQueryFilter(Expression{Func{TEntity, bool}})"/>
    /// because it merges the new filter with the existing one instead of replacing it.
    /// </summary>
    public static EntityTypeBuilder<TEntity> AndHasQueryFilter<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, bool>> filter
    )
        where TEntity : class
    {
        // INTERNAL EF CORE API USAGE
        // -----------------------------------------------------------------------------
        // Required: Access to CoreAnnotationNames.QueryFilter to retrieve existing query
        //   filter from entity metadata. EF Core's public API HasQueryFilter() replaces
        //   filters rather than combining them. We need the annotation name constant to
        //   retrieve the existing filter for AND-combination.
        // Tested with: EF Core 8.x, 9.x, 10.x
        // On EF Core upgrade: Verify CoreAnnotationNames.QueryFilter constant exists
        //   and FindAnnotation() returns the filter expression with this key.
        // Alternative: None available in public API as of EF Core 10.0
        // -----------------------------------------------------------------------------
#pragma warning disable EF1001 // Is an internal API
        var queryFilterAnnotation = builder.Metadata.FindAnnotation(CoreAnnotationNames.QueryFilter);
#pragma warning restore EF1001

        if (queryFilterAnnotation is { Value: Expression<Func<TEntity, bool>> existingFilter })
        {
            filter = filter.And(existingFilter);
        }

        return builder.HasQueryFilter(filter);
    }

    /// <inheritdoc cref="AndHasQueryFilter{TEntity}(EntityTypeBuilder{TEntity},Expression{Func{TEntity,bool}})"/>
    public static void AndHasQueryFilter<TEntity>(
        this EntityTypeBuilder builder,
        Expression<Func<TEntity, bool>> filter
    )
        where TEntity : class
    {
        // INTERNAL EF CORE API USAGE
        // -----------------------------------------------------------------------------
        // Required: Access to CoreAnnotationNames.QueryFilter to retrieve existing query
        //   filter from entity metadata. See documentation in generic overload above.
        // Tested with: EF Core 8.x, 9.x, 10.x
        // On EF Core upgrade: Verify CoreAnnotationNames.QueryFilter constant exists
        // Alternative: None available in public API as of EF Core 10.0
        // -----------------------------------------------------------------------------
#pragma warning disable EF1001 // Is an internal API
        var queryFilterAnnotation = builder.Metadata.FindAnnotation(CoreAnnotationNames.QueryFilter);
#pragma warning restore EF1001

        if (queryFilterAnnotation is { Value: Expression<Func<TEntity, bool>> existingFilter })
        {
            filter = filter.And(existingFilter);
        }

        builder.HasQueryFilter(filter);
    }
}
