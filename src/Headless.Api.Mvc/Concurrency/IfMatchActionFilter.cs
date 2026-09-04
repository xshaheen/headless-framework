// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api.Concurrency;

internal sealed class IfMatchActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ActionDescriptor.EndpointMetadata.OfType<RequireIfMatchAttribute>().Any())
        {
            _ = await next().ConfigureAwait(false);
            return;
        }

        var problem = IfMatchRequestValidator.Validate(context.HttpContext);
        if (problem is not null)
        {
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
            return;
        }
        _ = await next().ConfigureAwait(false);
    }
}
