// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Api.Mvc.Filters;

[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DevelopmentOnlyAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var environment = services.GetRequiredService<IWebHostEnvironment>();

        if (environment.IsDevelopment())
        {
            await next();

            return;
        }

        var factory = services.GetRequiredService<IFrameworkProblemDetailsFactory>();
        var problemDetails = factory.EndpointNotFound(context.HttpContext);
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext);
    }
}
