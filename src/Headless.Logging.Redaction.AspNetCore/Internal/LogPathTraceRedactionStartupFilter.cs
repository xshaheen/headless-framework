// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Headless.Logging.Internal;

/// <summary>
/// Replaces a declared credential path in the request span's tags as the first middleware, so the server span is
/// redacted whether or not the app's tracer provider runs <c>AddHeadlessLogPathRedaction</c>.
/// </summary>
internal sealed class LogPathTraceRedactionStartupFilter(IEnumerable<ILogPathRedactor> redactors) : IStartupFilter
{
    private readonly ILogPathRedactor[] _redactors = [.. redactors];

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            if (_redactors.Length > 0)
            {
                app.Use(
                    (context, nextMiddleware) =>
                    {
                        _Redact(context);

                        return nextMiddleware(context);
                    }
                );
            }

            next(app);
        };
    }

    private void _Redact(HttpContext context)
    {
        var request = context.Request;

        if (
            request.Path.Value is not { } path
            || !ILogPathRedactor.TryRedactAny(_redactors, path, out var redactedPath)
        )
        {
            return;
        }

        // Matches how ASP.NET Core and the OpenTelemetry instrumentation build url.path.
        var raw = request.PathBase.Add(request.Path).ToString();
        var redacted = request.PathBase + redactedPath;

        var serverActivity = context.Features.Get<IHttpActivityFeature>()?.Activity;

        if (serverActivity is not null)
        {
            LogPathTags.Replace(serverActivity, raw, redacted);
        }

        // The OpenTelemetry instrumentation exports a sibling of the server activity, and sets the tags on it, when
        // the incoming trace context does not match the one ASP.NET Core extracted.
        if (Activity.Current is { } current && !ReferenceEquals(current, serverActivity))
        {
            LogPathTags.Replace(current, raw, redacted);
        }
    }
}
