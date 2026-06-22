// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Api.Filters;

/// <summary>
/// Action filter that blocks access to an endpoint when the host environment matches
/// <see cref="Environment"/>. When the environment matches, the endpoint returns a 404 Not Found
/// problem-details response and the action is never invoked. All other environments pass through normally.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BlockInEnvironmentAttribute(string environment) : Attribute, IAsyncResourceFilter
{
    /// <summary>Gets the environment name in which the endpoint is blocked.</summary>
    public string Environment { get; } = environment;

    /// <inheritdoc/>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var env = services.GetRequiredService<IWebHostEnvironment>();

        if (!env.IsEnvironment(Environment))
        {
            await next().ConfigureAwait(false);

            return;
        }

        var factory = services.GetRequiredService<IProblemDetailsCreator>();
        var problemDetails = factory.EndpointNotFound();
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext).ConfigureAwait(false);
    }
}
