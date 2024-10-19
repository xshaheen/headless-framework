// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Api.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Api.Mvc.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var services = context.HttpContext.RequestServices;
        var environment = services.GetRequiredService<IWebHostEnvironment>();

        if (environment.IsDevelopment())
        {
            return;
        }

        var creator = services.GetRequiredService<IProblemDetailsCreator>();
        var endpointNotFound = creator.EndpointNotFound(context.HttpContext);
        context.Result = new NotFoundObjectResult(endpointNotFound);
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
