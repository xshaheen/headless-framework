// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Fencing;

namespace System.Linq;

/// <summary>Selects lease candidates by the database clock, for rows that keep their lease as columns.</summary>
[PublicAPI]
public static class HeadlessFencingQueryableExtensions
{
    /// <summary>
    /// Keeps the rows whose lease is live: <see cref="RowLeaseState.Active" /> and expiring after the database clock.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The query.</param>
    /// <param name="lease">The entity's lease property, the one <c>HasRowLease</c> maps.</param>
    /// <returns>The filtered query.</returns>
    /// <remarks>
    /// The clock is the one <c>UseFencingDatabaseClock</c> maps; without it the query fails to translate. The result
    /// selects candidates and decides nothing: a lease can expire, or be granted, between this read and the transition
    /// run on a candidate, and the transition re-checks the lease after its row lock.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    public static IQueryable<TEntity> WhereLeaseLive<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, RowLeaseSnapshot>> lease
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(lease);

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(_Live(lease.Body), lease.Parameters));
    }

    /// <summary>
    /// Keeps the rows whose lease can be granted: any row whose lease is not live, so idle, finished, or expired.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The query.</param>
    /// <param name="lease">The entity's lease property, the one <c>HasRowLease</c> maps.</param>
    /// <returns>The filtered query.</returns>
    /// <remarks>
    /// The clock is the one <c>UseFencingDatabaseClock</c> maps; without it the query fails to translate. The result
    /// selects candidates and decides nothing: the grant run on a candidate re-checks the lease after its row lock and
    /// returns <see langword="null" /> when another holder took it first.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    public static IQueryable<TEntity> WhereLeaseGrantable<TEntity>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, RowLeaseSnapshot>> lease
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(lease);

        return source.Where(
            Expression.Lambda<Func<TEntity, bool>>(Expression.Not(_Live(lease.Body)), lease.Parameters)
        );
    }

    private static BinaryExpression _Live(Expression lease)
    {
        return Expression.AndAlso(
            Expression.Equal(
                Expression.Property(lease, nameof(RowLeaseSnapshot.State)),
                Expression.Constant(RowLeaseState.Active)
            ),
            Expression.Call(
                FencingDatabaseClock.IsBeforeExpiryMethod,
                Expression.Property(lease, nameof(RowLeaseSnapshot.ExpiresAt))
            )
        );
    }
}
