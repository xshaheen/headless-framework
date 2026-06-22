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
internal sealed class ServerTimingMiddleware : IMiddleware
{
    private const string _ServerTimingHttpHeader = "Server-Timing";

    /// <summary>Processes the current request, measuring elapsed time and appending a <c>Server-Timing</c> trailer.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next middleware delegate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="next"/> is <see langword="null"/>.</exception>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(next);

        if (!context.Response.SupportsTrailers())
        {
            await next(context).ConfigureAwait(false);

            return;
        }

        context.Response.DeclareTrailer(_ServerTimingHttpHeader);

        var timestamp = Stopwatch.GetTimestamp();
        await next(context).ConfigureAwait(false);
        var elapsedTime = Stopwatch.GetElapsedTime(timestamp);

        // string.Create with the interpolated-string handler formats directly into a pooled buffer with
        // invariant culture, avoiding the FormattableString object + boxed object[] args allocation.
        var serverTiming = string.Create(
            CultureInfo.InvariantCulture,
            $"app;dur={(long)elapsedTime.TotalMicroseconds}.0"
        );
        context.Response.AppendTrailer(_ServerTimingHttpHeader, serverTiming);
    }
}
