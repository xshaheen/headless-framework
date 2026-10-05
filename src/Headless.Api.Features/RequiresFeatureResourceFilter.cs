// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Features;

/// <summary>
/// Global MVC filter that enforces <see cref="RequiresFeatureAttribute"/> on controllers and actions. It runs as a
/// resource filter, before model binding and validation, so a request to a disabled feature gets the feature error
/// rather than a validation error for a body it could never use.
/// </summary>
internal sealed class RequiresFeatureResourceFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var httpContext = context.HttpContext;

        // The endpoint's metadata carries the controller and action attributes plus any route conventions; the action
        // descriptor's copy covers hosts that dispatch MVC without endpoint routing.
        var metadata =
            (IReadOnlyList<object>?)httpContext.GetEndpoint()?.Metadata
            ?? context.ActionDescriptor.EndpointMetadata.ToArray();

        // IFeatureManager is transient, so it is resolved per request rather than captured by this shared instance.
        var featureManager = httpContext.RequestServices.GetRequiredService<IFeatureManager>();

        await FeatureRequirementMetadata
            .EnsureEnabledAsync(featureManager, metadata, httpContext.RequestAborted)
            .ConfigureAwait(false);

        await next().ConfigureAwait(false);
    }
}
