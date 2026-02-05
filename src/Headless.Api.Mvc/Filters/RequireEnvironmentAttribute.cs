// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Api.Filters;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireEnvironmentAttribute(string environment) : Attribute, IAsyncResourceFilter
{
    public string Environment { get; } = environment;

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
