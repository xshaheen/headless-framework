// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

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

        // Stash a typed feature on the IFeatureCollection so StatusCodesRewriterMiddleware can
        // substitute the structured g:tenant_required ProblemDetails body for the generic 403.
        // Using a typed feature avoids the string-key collision risk of HttpContext.Items, and
        // decouples the requirement from the authorization-result-handler pipeline — consumers can
        // register their own IAuthorizationMiddlewareResultHandler in any order without disabling
        // the discriminator.
        httpContext?.Features.Set(new TenantContextRequiredFeature());

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
