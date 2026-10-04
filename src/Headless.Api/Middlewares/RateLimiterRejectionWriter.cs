// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.RateLimiting;
using Headless.Abstractions;
using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>Writes the framework 429 response for a request the ASP.NET Core rate limiter rejected.</summary>
internal static class RateLimiterRejectionWriter
{
    /// <summary>
    /// The <c>Retry-After</c> value when the rejecting limiter publishes none. A concurrency limiter frees a permit when
    /// any in-flight request completes, so the shortest valid delay is the honest answer there.
    /// </summary>
    internal const int FallbackRetryAfterSeconds = 1;

    public static Task WriteAsync(OnRejectedContext context)
    {
        var httpContext = context.HttpContext;

        if (httpContext.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? RetryAfterSeconds.From(retryAfter)
            : FallbackRetryAfterSeconds;

        var problemDetails = httpContext
            .RequestServices.GetRequiredService<IProblemDetailsCreator>()
            .TooManyRequests(retryAfterSeconds, GeneralMessageDescriber.RateLimitExceeded());

        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        // A shared cache must never replay a rejection to a caller whose budget is open.
        httpContext.Response.Headers.CacheControl = "no-store";

        return TenantCatalogRejectionWriter.WriteAsync(
            httpContext,
            StatusCodes.Status429TooManyRequests,
            problemDetails
        );
    }
}
