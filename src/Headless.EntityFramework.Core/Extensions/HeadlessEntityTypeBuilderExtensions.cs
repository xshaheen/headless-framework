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
    /// <summary>Combines a query filter with any existing filter using a logical AND operation.</summary>
    public static EntityTypeBuilder<TEntity> AndHasQueryFilter<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, bool>> filter
    )
        where TEntity : class
    {
#pragma warning disable EF1001 // EF exposes no public API for reading the existing query-filter expression.
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
#pragma warning disable EF1001 // EF exposes no public API for reading the existing query-filter expression.
        var queryFilterAnnotation = builder.Metadata.FindAnnotation(CoreAnnotationNames.QueryFilter);
#pragma warning restore EF1001

        if (queryFilterAnnotation is { Value: Expression<Func<TEntity, bool>> existingFilter })
        {
            filter = filter.And(existingFilter);
        }

        builder.HasQueryFilter(filter);
    }
}
