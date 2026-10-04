// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.EntityFramework.Contexts;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

/// <summary>
/// The single source of the tenant value that tenant query filters compare against. Filters call it through
/// <see cref="CreateTenantAccess"/> so every tenant-aware filter shares one read-guard decision.
/// </summary>
internal static class HeadlessTenantQueryFilter
{
    private static readonly MethodInfo _GetTenantIdMethod = ((Func<DbContext, bool, string?>)GetTenantId).Method;

    /// <summary>
    /// Builds the filter expression that yields the executing context's tenant. The model is cached per context
    /// type, so only <paramref name="required"/> is baked in; the guard state is read on every execution.
    /// </summary>
    /// <param name="db">
    /// The context the model is being built for. EF replaces this constant with the executing context and
    /// evaluates the call client-side as a query parameter, so a guard failure surfaces when the query runs.
    /// </param>
    /// <param name="required">Whether the tenant column is required, which makes a missing tenant a guard failure.</param>
    public static Expression CreateTenantAccess(DbContext db, bool required) =>
        Expression.Call(_GetTenantIdMethod, Expression.Constant(db), Expression.Constant(required));

    internal static string? GetTenantId(DbContext db, bool required)
    {
        var context = (IHeadlessDbContext)db;
        var tenantId = context.TenantId;

        if (
            required
            && string.IsNullOrWhiteSpace(tenantId)
            && (
                db is IHeadlessDbContextRuntimeOwner owner
                    ? owner.Runtime.IsGuardReadsEnabled
                    : context.ServiceProvider.GetRequiredService<IOptions<TenantGuardOptions>>().Value.GuardReads
            )
        )
        {
            throw new MissingTenantContextException(
                $"A query over a tenant-owned entity of '{db.GetType().Name}' requires an ambient tenant. Set one "
                    + "with ICurrentTenant.Change(tenantId), or call IgnoreMultiTenancyFilter() for a deliberate "
                    + "cross-tenant read."
            );
        }

        return tenantId;
    }
}
