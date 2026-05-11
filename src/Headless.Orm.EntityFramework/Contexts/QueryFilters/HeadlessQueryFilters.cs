// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

[PublicAPI]
public static class HeadlessQueryFilters
{
    public const string MultiTenancyFilter = "MultiTenantFilter";
    public const string NotDeletedFilter = "NotDeletedFilter";
    public const string NotSuspendedFilter = "NotSuspendedFilter";

    extension<TEntity>(IQueryable<TEntity> source)
        where TEntity : class
    {
        public IQueryable<TEntity> IgnoreMultiTenancyFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(MultiTenancyFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([MultiTenancyFilter]);
        }

        public IQueryable<TEntity> IgnoreNotDeletedFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(NotDeletedFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([NotDeletedFilter]);
        }

        public IQueryable<TEntity> IgnoreNotSuspendedFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(NotSuspendedFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([NotSuspendedFilter]);
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
