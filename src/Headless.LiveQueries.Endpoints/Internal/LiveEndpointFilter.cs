// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.LiveQueries.Internal;

/// <summary>Hands a request to a live endpoint over to the host's <see cref="LiveStreamHandler" />.</summary>
internal sealed class LiveEndpointFilter : IEndpointFilter
{
    public static readonly LiveEndpointFilter Instance = new();

    private LiveEndpointFilter() { }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (httpContext.Features.Get<LiveRunMarker>() is not null)
        {
            return next(context);
        }

        return httpContext.RequestServices.GetRequiredService<LiveStreamHandler>().HandleAsync(context, next);
    }
}
