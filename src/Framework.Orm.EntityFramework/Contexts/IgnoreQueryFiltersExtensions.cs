// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Framework.Orm.EntityFramework.Contexts;

public static class HeadlessQueryFilters
{
    public const string MultiTenancyFilter = "MultiTenantFilter";
    public const string NotDeletedFilter = "NotDeletedFilter";
    public const string NotSuspendedFilter = "NotSuspendedFilter";
}

public static class IgnoreQueryFiltersExtensions
{
    public static IQueryable<TEntity> IgnoreMultiTenancyFilter<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
    {
        return source.IgnoreQueryFilters([HeadlessQueryFilters.MultiTenancyFilter]);
    }

    public static IQueryable<TEntity> IgnoreNotDeletedFilter<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
    {
        return source.IgnoreQueryFilters([HeadlessQueryFilters.NotDeletedFilter]);
    }

    public static IQueryable<TEntity> IgnoreNotSuspendedFilter<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class
    {
        return source.IgnoreQueryFilters([HeadlessQueryFilters.NotSuspendedFilter]);
    }
}
