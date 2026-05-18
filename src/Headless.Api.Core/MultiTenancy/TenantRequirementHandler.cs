// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

[PublicAPI]
public sealed class TenantRequirementHandler(ICurrentTenant currentTenant) : AuthorizationHandler<TenantRequirement>
{
    private readonly ICurrentTenant _currentTenant = Argument.IsNotNull(currentTenant);

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantRequirement requirement)
    {
        if (!string.IsNullOrWhiteSpace(_currentTenant.Id))
        {
            context.Succeed(requirement);

            return Task.CompletedTask;
        }

        if (
            context.Resource is HttpContext httpContext
            && httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowMissingTenantAttribute>() is not null
        )
        {
            context.Succeed(requirement);

            return Task.CompletedTask;
        }

        context.Fail(new AuthorizationFailureReason(this, TenantRequirement.FailureReason));

        return Task.CompletedTask;
    }
}
