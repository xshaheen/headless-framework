// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Features;

/// <summary>Endpoint filter behind <c>RequireFeatures</c>: enforces one requirement on a Minimal API endpoint.</summary>
/// <remarks>
/// Each <c>RequireFeatures</c> call adds its own filter with its own requirement, so a requirement on a route group and
/// another on an endpoint in it must both pass, and neither is checked twice.
/// </remarks>
internal sealed class RequiresFeatureEndpointFilter(RequiresFeatureAttribute requirement) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<DisableFeatureCheckAttribute>() is null)
        {
            var featureManager = httpContext.RequestServices.GetRequiredService<IFeatureManager>();

            await featureManager
                .EnsureEnabledAsync(requirement.IsAnd, requirement.Features, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        return await next(context).ConfigureAwait(false);
    }
}
