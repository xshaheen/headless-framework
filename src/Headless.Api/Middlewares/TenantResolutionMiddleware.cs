// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.Middlewares;

/// <summary>Resolves the current tenant from the authenticated principal for the lifetime of the HTTP request.</summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IOptions<MultiTenancyOptions> options)
{
    /// <summary>Resolves the tenant from the current user claims and restores the previous tenant when the request ends.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="currentTenant">The current tenant accessor.</param>
    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(currentTenant);

        if (context.User.Identity?.IsAuthenticated != true)
        {
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
}
