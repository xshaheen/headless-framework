// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

/// <summary>
/// Named global-query-filter constants and bypass extensions for the three Headless EF Core query
/// filters: multi-tenancy, soft-delete, and suspend.
/// </summary>
/// <remarks>
/// The bypass extensions emit a debug-level security audit trace before calling
/// <c>IgnoreQueryFilters</c>. Use them instead of calling <c>IgnoreQueryFilters</c> directly so
/// cross-filter bypasses are traceable.
/// </remarks>
[PublicAPI]
public static class HeadlessQueryFilters
{
    /// <summary>Named tag for the <c>IMultiTenant.TenantId</c> equality filter applied by the Headless runtime.</summary>
    public const string MultiTenancyFilter = "MultiTenantFilter";

    /// <summary>Named tag for the <c>IDeleteAudit.IsDeleted == false</c> soft-delete filter.</summary>
    public const string NotDeletedFilter = "NotDeletedFilter";

    /// <summary>Named tag for the <c>ISuspendAudit.IsSuspended == false</c> suspend filter.</summary>
    public const string NotSuspendedFilter = "NotSuspendedFilter";

    extension<TEntity>(IQueryable<TEntity> source)
        where TEntity : class
    {
        /// <summary>
        /// Returns the query with the multi-tenancy filter suppressed for this query only. Emits a
        /// debug-level security audit trace containing the call site.
        /// </summary>
        public IQueryable<TEntity> IgnoreMultiTenancyFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(MultiTenancyFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([MultiTenancyFilter]);
        }

        /// <summary>
        /// Returns the query with the soft-delete (<c>IsDeleted == false</c>) filter suppressed for this
        /// query only. Emits a debug-level security audit trace containing the call site.
        /// </summary>
        public IQueryable<TEntity> IgnoreNotDeletedFilter(
            [CallerMemberName] string callerMember = "",
            [CallerFilePath] string callerFile = ""
        )
        {
            _LogFilterBypassed(NotDeletedFilter, typeof(TEntity).Name, callerMember, callerFile);
            return source.IgnoreQueryFilters([NotDeletedFilter]);
        }

        /// <summary>
        /// Returns the query with the suspend (<c>IsSuspended == false</c>) filter suppressed for this
        /// query only. Emits a debug-level security audit trace containing the call site.
        /// </summary>
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
