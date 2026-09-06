// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api.Concurrency;

internal sealed class IfMatchActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var endpointMetadata = context.ActionDescriptor.EndpointMetadata;
        var requiresIfMatch = false;
        for (var index = 0; index < endpointMetadata.Count; index++)
        {
            if (endpointMetadata[index] is RequireIfMatchAttribute)
            {
                requiresIfMatch = true;
                break;
            }
        }

        if (!requiresIfMatch)
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
