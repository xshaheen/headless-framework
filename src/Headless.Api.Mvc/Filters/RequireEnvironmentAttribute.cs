// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Api.Filters;

/// <summary>
/// Action filter that restricts an endpoint to a specific host environment. When the current
/// environment does not match <see cref="Environment"/>, the endpoint returns a 404 Not Found
/// problem-details response and the action is never invoked. Matching environments pass through normally.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireEnvironmentAttribute(string environment) : Attribute, IAsyncResourceFilter
{
    /// <summary>Gets the environment name required to access the endpoint.</summary>
    public string Environment { get; } = environment;

    /// <inheritdoc/>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var env = services.GetRequiredService<IWebHostEnvironment>();

        if (env.IsEnvironment(Environment))
        {
            await next().ConfigureAwait(false);

            return;
        }

        var factory = services.GetRequiredService<IProblemDetailsCreator>();
        var problemDetails = factory.EndpointNotFound();
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext).ConfigureAwait(false);
    }
}
