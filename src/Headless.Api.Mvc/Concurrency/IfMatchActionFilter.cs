// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Headless.Api;

internal sealed class IfMatchActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_RequiresIfMatch(context))
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

    // The routed endpoint's metadata holds the action's attributes plus what a MapControllers() convention added, which
    // the action descriptor never sees; the descriptor is the fallback for a request that was not endpoint-routed.
    private static bool _RequiresIfMatch(ActionExecutingContext context)
    {
        if (context.HttpContext.GetEndpoint() is { } endpoint)
        {
            return endpoint.Metadata.GetMetadata<RequireIfMatchAttribute>() is not null;
        }

        var actionMetadata = context.ActionDescriptor.EndpointMetadata;

        for (var index = 0; index < actionMetadata.Count; index++)
        {
            if (actionMetadata[index] is RequireIfMatchAttribute)
            {
                return true;
            }
        }

        return false;
    }
}
