// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Authorization;

/// <summary>Effective permissions of a dashboard caller after authentication.</summary>
internal readonly record struct DashboardCaller(bool IsAuthenticated, bool CanRead, bool IsAdmin)
{
    internal static readonly DashboardCaller Anonymous = new(IsAuthenticated: false, CanRead: false, IsAdmin: false);
    internal static readonly DashboardCaller FullAccess = new(IsAuthenticated: true, CanRead: true, IsAdmin: true);
}

/// <summary>
/// Single permission decision point for the Jobs dashboard: the <c>/api</c> group endpoint filter, the
/// tenant-row handlers, and the SignalR hub all resolve the caller through this type so the rules cannot drift.
/// </summary>
internal sealed class JobsDashboardAuthorizer(DashboardOptionsBuilder options)
{
    private readonly DashboardOptionsBuilder _options = Argument.IsNotNull(options);

    /// <summary>
    /// Resolves the caller for an HTTP request that already passed the mode's authentication step
    /// (host authorization middleware or <see cref="AuthMiddleware"/>).
    /// </summary>
    internal DashboardCaller Resolve(HttpContext context)
    {
        return Resolve(context, _IsAuthenticated(context));
    }

    /// <summary>
    /// Resolves the caller given an externally established authentication outcome — used by the hub, whose path is
    /// outside <c>/api</c> and therefore authenticates itself through <see cref="IAuthService"/>.
    /// </summary>
    internal DashboardCaller Resolve(HttpContext context, bool authenticated)
    {
        if (_options.Auth.Mode == AuthMode.None)
        {
            // Explicit WithNoAuth(): documented development / trusted-network all-access opt-out.
            return DashboardCaller.FullAccess;
        }

        if (!authenticated)
        {
            return DashboardCaller.Anonymous;
        }

        if (_options.Auth.Mode != AuthMode.Host)
        {
            // Basic / ApiKey / Custom carry one shared credential and cannot express per-user permissions:
            // an authenticated caller keeps the historical all-access behavior.
            return DashboardCaller.FullAccess;
        }

        var claimType = _options.PermissionClaimType;
        var isAdmin = context.User.HasClaim(claimType, JobsDashboardPermissions.Admin);
        var canRead = isAdmin || context.User.HasClaim(claimType, JobsDashboardPermissions.Read);

        return new DashboardCaller(IsAuthenticated: true, CanRead: canRead, IsAdmin: isAdmin);
    }

    /// <summary>
    /// Authorizes a row-scoped time-job mutation against the <b>persisted</b> row tenant. Admin always passes.
    /// Otherwise the caller's resolved <see cref="ICurrentTenant.Id"/> must be present and equal the row tenant;
    /// a system-scope row (<see langword="null"/> tenant) is admin-only, and a caller without an ambient tenant never
    /// matches a tenant-owned row.
    /// </summary>
    internal static bool CanMutateTimeJob(HttpContext context, DashboardCaller caller, string? persistedTenantId)
    {
        if (caller.IsAdmin)
        {
            return true;
        }

        if (!caller.CanRead || persistedTenantId is null)
        {
            return false;
        }

        var currentTenantId = context.RequestServices.GetService<ICurrentTenant>()?.Id;

        return currentTenantId is not null
            && string.Equals(currentTenantId, persistedTenantId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Endpoint filter applied to the <c>/api</c> group. Returns <c>401</c> for an unauthenticated caller and
    /// <c>403</c> when the caller lacks the route's access class. An unclassified route fails closed with <c>403</c>;
    /// the classification test guarantees that cannot happen for a mapped route.
    /// </summary>
    internal ValueTask<object?> EnforceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var access = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<DashboardAccessMetadata>()?.Access;

        if (access is DashboardAccess.Anonymous)
        {
            return next(context);
        }

        var caller = Resolve(context.HttpContext);

        if (!caller.IsAuthenticated)
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        var allowed = access switch
        {
            DashboardAccess.Read or DashboardAccess.TenantRowMutation => caller.CanRead,
            DashboardAccess.Admin => caller.IsAdmin,
            _ => false,
        };

        return allowed
            ? next(context)
            : ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));
    }

    private bool _IsAuthenticated(HttpContext context)
    {
        return _options.Auth.Mode switch
        {
            AuthMode.None => true,
            AuthMode.Host => context.User.Identity?.IsAuthenticated == true,
            // AuthMiddleware stamps this flag after validating the shared credential on every /api request.
            _ => context.Items.TryGetValue(AuthMiddleware.AuthenticatedKey, out var flag) && flag is true,
        };
    }
}
