// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Domain;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Opts entity types into the Headless query filters that are not applied by default.</summary>
[PublicAPI]
public static class HeadlessQueryFilterPolicyExtensions
{
    /// <summary>
    /// Hides suspended rows of this entity type from queries through the named
    /// <see cref="HeadlessQueryFilters.NotSuspendedFilter"/>. Bypass it per query with
    /// <c>IgnoreNotSuspendedFilter()</c>.
    /// </summary>
    /// <remarks>
    /// Suspension is a business state, so no entity type is filtered by default. Opt in only when suspended rows
    /// must never reach ordinary reads, and remember that a required navigation to a filtered principal turns into
    /// an inner join that drops the dependent row too.
    /// </remarks>
    /// <param name="builder">The entity type builder of a root entity type that implements <see cref="ISuspendAudit"/>.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    /// <exception cref="ArgumentException">The entity type does not implement <see cref="ISuspendAudit"/>.</exception>
    public static EntityTypeBuilder HasNotSuspendedFilter(this EntityTypeBuilder builder)
    {
        var clrType = builder.Metadata.ClrType;
        Argument.IsTrue(
            clrType.IsAssignableTo(typeof(ISuspendAudit)),
            $"The entity type '{clrType.Name}' must implement {nameof(ISuspendAudit)} to use the not-suspended filter.",
            nameof(builder)
        );

        var entity = Expression.Parameter(clrType, "x");
        var isSuspended = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(bool)],
            entity,
            Expression.Constant(nameof(ISuspendAudit.IsSuspended))
        );

        return builder.HasQueryFilter(
            HeadlessQueryFilters.NotSuspendedFilter,
            Expression.Lambda(Expression.Not(isSuspended), entity)
        );
    }

    /// <inheritdoc cref="HasNotSuspendedFilter(EntityTypeBuilder)"/>
    /// <typeparam name="TEntity">The root entity type.</typeparam>
    public static EntityTypeBuilder<TEntity> HasNotSuspendedFilter<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISuspendAudit
    {
        return builder.HasQueryFilter(
            HeadlessQueryFilters.NotSuspendedFilter,
            x => !EF.Property<bool>(x, nameof(ISuspendAudit.IsSuspended))
        );
    }
}
