// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.ServiceDefaults;

/// <summary>
/// The middleware stages <see cref="SetupApi.UseHeadless(Microsoft.AspNetCore.Builder.WebApplication, Action{HeadlessApiDefaultsOptions}?)"/>
/// applies, in pipeline order. Use them as anchors for
/// <see cref="HeadlessApiDefaultsOptions.InsertBefore"/> and <see cref="HeadlessApiDefaultsOptions.InsertAfter"/>.
/// </summary>
/// <remarks>
/// An earlier stage runs outside a later one: it sees the request first and the response last.
/// </remarks>
public enum HeadlessPipelineStage
{
    /// <summary>Forwarded-headers middleware, so every later stage sees the client's scheme, host, and IP.</summary>
    ForwardedHeaders = 0,

    /// <summary>Response compression, outside everything that writes or records a response body.</summary>
    ResponseCompression = 1,

    /// <summary>Status-code pages and the Headless status-code rewriter for bare error responses.</summary>
    StatusCodePages = 2,

    /// <summary>The exception handler that turns exceptions into ProblemDetails.</summary>
    ExceptionHandler = 3,

    /// <summary>HTTPS redirection. Skipped in the Development and Test environments.</summary>
    HttpsRedirection = 4,

    /// <summary>HSTS. Skipped in the Development and Test environments.</summary>
    Hsts = 5,

    /// <summary>The no-cache header for responses that set no <c>Cache-Control</c>.</summary>
    NoCacheHeaders = 6,
}
