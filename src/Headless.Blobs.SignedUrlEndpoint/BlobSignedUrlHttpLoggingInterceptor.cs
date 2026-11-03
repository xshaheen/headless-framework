// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;

namespace Headless.Blobs;

/// <summary>Logs a signed blob URL's path with its token replaced by <c>***</c> in HTTP logging.</summary>
/// <remarks>
/// Registering it costs nothing when the app does not call <c>UseHttpLogging</c>. It matches every method, not only the
/// mapped GET and PUT, because a HEAD or POST to the same path carries the same token.
/// </remarks>
internal sealed class BlobSignedUrlHttpLoggingInterceptor(BlobSignedUrlPathRedactor redactor) : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext)
    {
        var request = logContext.HttpContext.Request;

        // The middleware logs PathBase and Path together under RequestPath, so both are added back in its shape.
        if (redactor.TryRedact(request.Path, out var redacted) && logContext.TryDisable(HttpLoggingFields.RequestPath))
        {
            logContext.AddParameter(nameof(HttpRequest.PathBase), request.PathBase);
            logContext.AddParameter(nameof(HttpRequest.Path), redacted);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
    {
        return ValueTask.CompletedTask;
    }
}
