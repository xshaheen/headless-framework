// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;

namespace Headless.Logging.Internal;

/// <summary>Replaces a declared credential path in the request record ASP.NET Core's HTTP logging writes.</summary>
internal sealed class LogPathRedactionHttpLoggingInterceptor(IEnumerable<ILogPathRedactor> redactors)
    : IHttpLoggingInterceptor
{
    private readonly ILogPathRedactor[] _redactors = [.. redactors];

    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        var request = logContext.HttpContext.Request;

        if (request.Path.Value is not { } path)
        {
            return ValueTask.CompletedTask;
        }

        // The middleware logs PathBase and Path together under RequestPath, so both are added back in its shape, as
        // PathString values.
        if (
            ILogPathRedactor.TryRedactAny(_redactors, path, out var redacted)
            && logContext.TryDisable(HttpLoggingFields.RequestPath)
        )
        {
            logContext.AddParameter(nameof(HttpRequest.PathBase), request.PathBase);
            logContext.AddParameter(nameof(HttpRequest.Path), new PathString(redacted));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
    {
        return ValueTask.CompletedTask;
    }
}
