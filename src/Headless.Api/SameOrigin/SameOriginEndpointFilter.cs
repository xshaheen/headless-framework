// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api;

internal sealed class SameOriginEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var problem = SameOriginRequestValidator.Validate(context.HttpContext);
        return problem is null ? await next(context).ConfigureAwait(false) : TypedResults.Problem(problem);
    }
}
