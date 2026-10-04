// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Headless.Api;

internal sealed class TenantRequirementHandler(ICurrentTenant currentTenant) : AuthorizationHandler<TenantRequirement>
{
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;

        if (
            !string.IsNullOrWhiteSpace(_currentTenant.Id)
            || (httpContext is not null && _AllowsMissingTenant(httpContext.GetEndpoint()))
        )
        {
            context.Succeed(requirement);

            return Task.CompletedTask;
        }

        // Hand the rejection to StatusCodesRewriterMiddleware through the request feature rather than a
        // result handler, so consumers can register their own IAuthorizationMiddlewareResultHandler in any
        // order without disabling the g:tenant_required discriminator. TrySet keeps a rejection another
        // handler already chose, such as the identifier mismatch.
        httpContext?.TrySetStatusCodeRejection(TenantContextRequiredFeature.Instance);

        context.Fail(new AuthorizationFailureReason(this, TenantRequirement.FailureReason));

        return Task.CompletedTask;
    }

    private static bool _AllowsMissingTenant(Endpoint? endpoint)
    {
        if (endpoint is null)
        {
            return false;
        }

        // GetMetadata<T>() returns the last registered metadata of the type (last-wins),
        // matching ASP.NET Core's own attribute-ordering semantics. When both attributes are
        // present, determine which was registered later by scanning for the index of each.
        var allow = endpoint.Metadata.GetMetadata<AllowMissingTenantAttribute>();
        var require = endpoint.Metadata.GetMetadata<RequireTenantAttribute>();

        if (allow is null)
        {
            return false;
        }

        if (require is null)
        {
            return true;
        }

        // Both present — the later registration wins.
        var metadata = endpoint.Metadata;

        for (var i = metadata.Count - 1; i >= 0; i--)
        {
            if (metadata[i] is RequireTenantAttribute)
            {
                return false;
            }

            if (metadata[i] is AllowMissingTenantAttribute)
            {
                return true;
            }
        }

        return false;
    }
}
