// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Headless.Blobs;

/// <summary>
/// Puts a middleware at the front of the pipeline that replaces a signed blob URL's token with <c>***</c> in the
/// request activity's path tags.
/// </summary>
/// <remarks>
/// ASP.NET Core and the OpenTelemetry instrumentation set <c>url.path</c> when the activity starts, before any
/// middleware, and neither sets it again when it stops, so exporters see the rewritten value. It runs ahead of routing
/// so that a request the endpoint does not map, such as a HEAD with a live token, is rewritten too.
/// </remarks>
internal sealed class BlobSignedUrlTraceRedactionStartupFilter(BlobSignedUrlPathRedactor redactor) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(
                (context, nextMiddleware) =>
                {
                    redactor.RedactActivities(context);

                    return nextMiddleware(context);
                }
            );

            next(app);
        };
    }
}
