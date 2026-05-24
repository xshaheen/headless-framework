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

        // Stash a marker on HttpContext.Items so StatusCodesRewriterMiddleware can substitute the
        // structured g:tenant_required ProblemDetails body for the generic 403. This avoids
        // decorating IAuthorizationMiddlewareResultHandler, which is sensitive to DI registration
        // order — consumers can now register their own result handler in any order without
        // disabling the discriminator.
        httpContext?.Items[TenantRequirement.HttpContextItemKey] = true;

        context.Fail(new AuthorizationFailureReason(this, TenantRequirement.FailureReason));

        return Task.CompletedTask;
    }

    private static bool _AllowsMissingTenant(Endpoint? endpoint)
    {
        if (endpoint is null)
        {
            return false;
        }

        foreach (var metadata in endpoint.Metadata.Reverse())
        {
            switch (metadata)
            {
                case RequireTenantAttribute:
                    return false;

                case AllowMissingTenantAttribute:
                    return true;
            }
        }

        return false;
    }
}
