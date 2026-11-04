// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

/// <summary>Guards dashboard endpoints against cross-site requests that change state.</summary>
internal static class DashboardRequestIntegrity
{
    /// <summary>The header every state-changing dashboard request must carry.</summary>
    internal const string RequestedWithHeader = "X-Requested-With";

    /// <summary>
    /// Refuses a state-changing request to the endpoints of <paramref name="builder"/> with <c>400</c> unless it
    /// carries <see cref="RequestedWithHeader"/>.
    /// </summary>
    internal static TBuilder RequireDashboardRequestIntegrity<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(RequireRequestedWithOnStateChange);
    }

    // A browser attaches cookies to a cross-site <form> post, so under cookie authentication another site could
    // otherwise stop the scheduler, requeue messages, or plant a session. A form cannot set a request header, and a
    // cross-origin script that sets one needs a CORS preflight the dashboard refuses, so the header limits state
    // changes to the dashboard SPA, its only client. Reads need no header: they change nothing.
    internal static ValueTask<object?> RequireRequestedWithOnStateChange(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next
    )
    {
        var request = invocationContext.HttpContext.Request;
        var isRead =
            HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method);

        if (isRead || request.Headers.ContainsKey(RequestedWithHeader))
        {
            return next(invocationContext);
        }

        return ValueTask.FromResult<object?>(
            Results.BadRequest($"State-changing dashboard requests must carry the {RequestedWithHeader} header.")
        );
    }
}
