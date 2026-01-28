// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Middlewares;

/// <summary>
/// Measures the time the request takes to process and returns this in a Server-Timing trailing HTTP header. It is
/// used to surface any back-end server timing metrics (e.g. database read/write, CPU time, file system access,
/// etc.) to the developer tools in the user's browser.
/// </summary>
/// <seealso cref="IMiddleware" />
public sealed class ServerTimingMiddleware : IMiddleware
{
    private const string _ServerTimingHttpHeader = "Server-Timing";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        if (!context.Response.SupportsTrailers())
        {
            await next(context).AnyContext();

            return;
        }

        context.Response.DeclareTrailer(_ServerTimingHttpHeader);

        var timestamp = Stopwatch.GetTimestamp();
        await next(context).AnyContext();
        var elapsedTime = Stopwatch.GetElapsedTime(timestamp);

        FormattableString serverTiming = $"app;dur={(long)elapsedTime.TotalMicroseconds}.0";
        context.Response.AppendTrailer(_ServerTimingHttpHeader, serverTiming.ToString(CultureInfo.InvariantCulture));
    }
}
