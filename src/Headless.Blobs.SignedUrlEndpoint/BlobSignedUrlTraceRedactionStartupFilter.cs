// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Blobs;

/// <summary>
/// Puts a middleware at the front of the pipeline that replaces a signed blob URL's token with <c>***</c> in the
/// request activity's path tags, and warns once when the hosting request logs are not redacted.
/// </summary>
/// <remarks>
/// ASP.NET Core and the OpenTelemetry instrumentation set <c>url.path</c> when the activity starts, before any
/// middleware, and neither sets it again when it stops, so exporters see the rewritten value. It runs ahead of routing
/// so that a request the endpoint does not map, such as a HEAD with a live token, is rewritten too.
/// </remarks>
internal sealed class BlobSignedUrlTraceRedactionStartupFilter(
    BlobSignedUrlPathRedactor redactor,
    ILoggerFactory loggerFactory
) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        // A logger factory registered after UseSignedUrlEndpoint replaces the decorator, and the hosting layer then
        // writes live tokens to every log. The check reads one registration; it never writes anything.
        if (loggerFactory is not BlobSignedUrlRedactingLoggerFactory)
        {
            loggerFactory.CreateLogger<BlobSignedUrlTraceRedactionStartupFilter>().LogHostingLogsNotRedacted();
        }

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

internal static partial class BlobSignedUrlLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SignedUrlHostingLogsNotRedacted",
        Level = LogLevel.Warning,
        Message = "Signed blob URL tokens are not redacted from ASP.NET Core's request scope and \"Request starting\"/\"Request finished\" lines: a logger factory was registered after UseSignedUrlEndpoint. Call UseSignedUrlEndpoint after the logger factory is registered (for example after UseSerilog), or set Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics to None."
    )]
    internal static partial void LogHostingLogsNotRedacted(this ILogger logger);
}
