// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Concurrency;

internal sealed class EntityTagResponseEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context).ConfigureAwait(false);
        var unwrappedResult = result;
        while (unwrappedResult is INestedHttpResult nested)
        {
            unwrappedResult = nested.Result;
        }

        var entityTagged = unwrappedResult switch
        {
            IHasEntityTag direct => direct,
            IValueHttpResult { Value: IHasEntityTag value } => value,
            _ => null,
        };

        EntityTagResponseWriter.Register(context.HttpContext, entityTagged);

        return result;
    }
}
