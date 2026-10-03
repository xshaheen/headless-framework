// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Middlewares;
using Headless.Checks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.RateLimiting;

/// <summary>Extension methods for <see cref="RateLimiterOptions"/>.</summary>
[PublicAPI]
public static class HeadlessRateLimiterOptionsExtensions
{
    /// <summary>
    /// Rejects rate-limited requests with the framework's 429 ProblemDetails: <c>error.code</c>
    /// <c>g:rate_limit_exceeded</c>, <c>retryAfter</c> in the body, a <c>Retry-After</c> header in whole seconds
    /// (rounded up, at least 1), and <c>Cache-Control: no-store</c>.
    /// </summary>
    /// <param name="options">The rate limiter options.</param>
    /// <returns>The same options for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Sets <see cref="RateLimiterOptions.OnRejected"/> and <see cref="RateLimiterOptions.RejectionStatusCode"/>. A
    /// policy with its own <c>OnRejected</c> still overrides this for its endpoints.
    /// </para>
    /// <para>
    /// The retry delay comes from the rejected lease's <see cref="System.Threading.RateLimiting.MetadataName.RetryAfter"/>.
    /// Fixed-window, sliding-window, and token-bucket limiters publish it; when a limiter publishes none, such as a
    /// concurrency limiter, the delay is one second. Requires the Headless API services that register
    /// <c>IProblemDetailsCreator</c>.
    /// </para>
    /// </remarks>
    public static RateLimiterOptions UseHeadlessProblemDetails(this RateLimiterOptions options)
    {
        Argument.IsNotNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = static (context, _) => new ValueTask(RateLimiterRejectionWriter.WriteAsync(context));

        return options;
    }
}
