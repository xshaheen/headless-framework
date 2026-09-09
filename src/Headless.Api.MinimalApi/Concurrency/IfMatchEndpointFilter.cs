// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.Concurrency;

internal sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var problem = IfMatchRequestValidator.Validate(context.HttpContext);
        return problem is null ? await next(context).ConfigureAwait(false) : TypedResults.Problem(problem);
    }
}
