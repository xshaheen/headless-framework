// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Headless.Api.Middlewares;

/// <summary>
/// Middleware that intercepts bare 401, 403, and 404 responses without a body and rewrites them
/// as structured <c>application/problem+json</c> ProblemDetails responses.
/// </summary>
/// <remarks>
/// A request carrying an <see cref="IStatusCodeRejectionFeature"/> is offered to that feature first,
/// whatever status the pipeline produced and even when an upstream component already set a
/// <c>Content-Type</c> or <c>Content-Length</c>: the handler that failed the request knows what its
/// rejection must look like, and a feature that accepts owns the whole response. A feature that declines
/// falls through to the bare-status rewrite below.
/// All writes are routed through <see cref="Microsoft.AspNetCore.Http.IProblemDetailsService"/> when
/// registered, falling back to <c>Results.Problem</c> for minimal-host scenarios.
/// </remarks>
internal sealed class StatusCodesRewriterMiddleware(IProblemDetailsCreator problemDetailsCreator) : IMiddleware
{
    /// <summary>Executes the middleware, rewriting qualifying error responses as ProblemDetails.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="next">The next middleware delegate.</param>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context).ConfigureAwait(false);

        if (context.Response.HasStarted)
        {
            return;
        }

        // Evaluated before the status-code gate below: a rejection may need to override a status that is not
        // an error at all (a cookie-style scheme forbids with a 302), and the rejection's owner, not this
        // middleware, decides whether it applies.
        if (
            context.Features.Get<IStatusCodeRejectionFeature>() is { } rejection
            && await rejection.TryWriteResponseAsync(context).ConfigureAwait(false)
        )
        {
            return;
        }

        var isNonError = context.Response.StatusCode is < 400 or >= 600;

        if (isNonError)
        {
            return;
        }

        // An upstream component (a consumer's IAuthorizationMiddlewareResultHandler, an endpoint) that already
        // chose a body keeps it: rewriting would clobber an intentional response.
        if (context.Response.ContentLength.HasValue || !string.IsNullOrEmpty(context.Response.ContentType))
        {
            return;
        }

        var problemDetails = context.Response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => problemDetailsCreator.Unauthorized(),
            StatusCodes.Status403Forbidden => problemDetailsCreator.Forbidden(),
            StatusCodes.Status404NotFound => problemDetailsCreator.EndpointNotFound(),
            _ => null,
        };

        if (problemDetails is null)
        {
            return;
        }

        // WriteAsync assigns the status code it is handed before writing; the status already matches the one
        // being rewritten, so this re-asserts it rather than changing it.
        await TenantCatalogRejectionWriter
            .WriteAsync(context, context.Response.StatusCode, problemDetails)
            .ConfigureAwait(false);
    }
}
