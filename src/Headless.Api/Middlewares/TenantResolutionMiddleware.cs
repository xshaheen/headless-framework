// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Middlewares;

/// <summary>Resolves the current tenant from the authenticated principal for the lifetime of the HTTP request.</summary>
public sealed partial class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<MultiTenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger
)
{
    // Fires exactly once per process for HEADLESS_TENANCY_MIDDLEWARE_ORDERING. 0 = not yet warned,
    // 1 = warned. CompareExchange ensures the warning is emitted by at most one request.
    private static int _orderingWarningEmitted;

    /// <summary>Resolves the tenant from the current user claims and restores the previous tenant when the request ends.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="currentTenant">The current tenant accessor.</param>
    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(currentTenant);

        context.Features.Set(HeadlessTenancyResolutionApplied.Instance);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            _WarnIfMiddlewareLikelyMisordered(context);
            await next(context);
            return;
        }

        var tenantId = _GetTenantId(context.User);

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await next(context);
            return;
        }

        using var _ = currentTenant.Change(tenantId);
        await next(context);
    }

    private string? _GetTenantId(ClaimsPrincipal principal)
    {
        Argument.IsNotNull(principal);

        var claimType = string.IsNullOrWhiteSpace(options.Value.ClaimType)
            ? UserClaimTypes.TenantId
            : options.Value.ClaimType;

        return string.Equals(claimType, UserClaimTypes.TenantId, StringComparison.Ordinal)
            ? principal.GetTenantId()
            : principal.FindFirst(claimType)?.Value;
    }

    private void _WarnIfMiddlewareLikelyMisordered(HttpContext context)
    {
        // If AuthenticationMiddleware has not run yet, the consumer almost certainly placed
        // UseHeadlessTenancy() ahead of UseAuthentication(). Warn once per process to avoid log spam.
        if (context.Features.Get<IAuthenticationFeature>() is not null)
        {
            return;
        }

        if (Volatile.Read(ref _orderingWarningEmitted) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _orderingWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        LogMiddlewareOrderingWarning(logger);
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "HEADLESS_TENANCY_MIDDLEWARE_ORDERING",
        Level = LogLevel.Warning,
        Message = "UseHeadlessTenancy() observed an unauthenticated request. If your endpoints expect a "
            + "claims-resolved tenant, place UseHeadlessTenancy() AFTER UseAuthentication() (and before "
            + "UseAuthorization()). This warning is emitted once per process."
    )]
    private static partial void LogMiddlewareOrderingWarning(ILogger logger);
}

/// <summary>
/// Marker feature set on the current HTTP request when <see cref="TenantResolutionMiddleware"/> has
/// executed for it. Consumed by <c>HeadlessApiExceptionHandler</c> to surface a runtime warning when
/// a <see cref="MissingTenantContextException"/> is raised on a request that never passed through
/// <c>UseHeadlessTenancy()</c>.
/// </summary>
internal sealed class HeadlessTenancyResolutionApplied
{
    public static HeadlessTenancyResolutionApplied Instance { get; } = new();

    private HeadlessTenancyResolutionApplied() { }
}
