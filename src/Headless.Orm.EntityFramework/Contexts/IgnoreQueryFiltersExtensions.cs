// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Headless.Orm.EntityFramework.Contexts;

public static class HeadlessQueryFilters
{
    public const string MultiTenancyFilter = "MultiTenantFilter";
    public const string NotDeletedFilter = "NotDeletedFilter";
    public const string NotSuspendedFilter = "NotSuspendedFilter";
}

public static class IgnoreQueryFiltersExtensions
{
    extension<TEntity>(IQueryable<TEntity> source)
        where TEntity : class
    {
        public IQueryable<TEntity> IgnoreMultiTenancyFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(HeadlessQueryFilters.MultiTenancyFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([HeadlessQueryFilters.MultiTenancyFilter]);
        }

        public IQueryable<TEntity> IgnoreNotDeletedFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(HeadlessQueryFilters.NotDeletedFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([HeadlessQueryFilters.NotDeletedFilter]);
        }

        public IQueryable<TEntity> IgnoreNotSuspendedFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(HeadlessQueryFilters.NotSuspendedFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([HeadlessQueryFilters.NotSuspendedFilter]);
        }
    }

    private static void _LogFilterBypassed(string filterName, string entityType, string callerMember, string callerFile)
    {
        var fileName = Path.GetFileName(callerFile);
        Debug.WriteLine(
            $"[SECURITY AUDIT] Query filter '{filterName}' bypassed for '{entityType}' from {callerMember} in {fileName}"
        );
    }
}
