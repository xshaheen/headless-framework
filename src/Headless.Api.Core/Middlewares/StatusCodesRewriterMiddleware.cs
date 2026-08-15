// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api.Middlewares;

/// <summary>
/// Middleware that intercepts bare 401, 403, and 404 responses without a body and rewrites them
/// as structured <c>application/problem+json</c> ProblemDetails responses.
/// </summary>
/// <remarks>
/// For 403 responses that carry a <c>TenantContextRequiredFeature</c> on the request, the middleware
/// clears any partial response and writes a <c>g:tenant_required</c> discriminator body, overriding
/// any <c>Content-Type</c> or <c>Content-Length</c> set by upstream authorization middleware.
/// A request carrying a <c>TenantIdentifierMismatchFeature</c> (R19) is rewritten whatever status the
/// authorization pipeline produced — not only a bare 403, since a cookie-style scheme forbids with a
/// 302 — because the secure-by-default mismatch rejection must stay byte-identical to the generic
/// unknown/disabled rejection (<see cref="TenantCatalogRejectionWriter.BuildMismatch"/>). Both the
/// status and the body are overridden.
/// All writes are routed through <see cref="Microsoft.AspNetCore.Http.IProblemDetailsService"/> when
/// registered, falling back to <c>Results.Problem</c> for minimal-host scenarios.
/// </remarks>
internal sealed class StatusCodesRewriterMiddleware(
    IProblemDetailsCreator problemDetailsCreator,
    IOptions<TenantCatalogOptions> catalogOptions
) : IMiddleware
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

        // TenantIdentifierIntegrityHandler stashes this marker on an R19 mismatch. The secure-by-default
        // rejection must be byte-identical to the generic unknown/disabled rejection, so it is evaluated
        // before — and independently of — the status-code switch below: the status the authorization
        // pipeline produced is not necessarily a bare 403 (a cookie-style scheme forbids with a 302), and
        // any surviving difference is exactly the enumeration signal R11/KTD9 exists to remove. Both the
        // status and the body are overridden here, not just the body.
        if (context.Features.Get<TenantIdentifierMismatchFeature>() is not null)
        {
            context.Response.Clear();

            var (mismatchStatusCode, mismatchProblemDetails) = TenantCatalogRejectionWriter.BuildMismatch(
                problemDetailsCreator,
                catalogOptions.Value.DetailedResolutionErrors
            );

            await TenantCatalogRejectionWriter
                .WriteAsync(context, mismatchStatusCode, mismatchProblemDetails)
                .ConfigureAwait(false);

            return;
        }

        var isNonError = context.Response.StatusCode is < 400 or >= 600;

        if (isNonError)
        {
            return;
        }

        // A consumer's IAuthorizationMiddlewareResultHandler may have already written a body (e.g.
        // set Content-Type before the 403 status was committed). When the tenant feature is present
        // we own the response — clear whatever partial headers were set and overwrite with the
        // structured g:tenant_required body. For every other status we honour the existing
        // Content-Type / Content-Length skip so we don't clobber intentional upstream responses.
        var hasTenantFeature =
            context.Response.StatusCode == StatusCodes.Status403Forbidden
            && context.Features.Get<TenantContextRequiredFeature>() is not null;

        if (
            !hasTenantFeature
            && (context.Response.ContentLength.HasValue || !string.IsNullOrEmpty(context.Response.ContentType))
        )
        {
            return;
        }

        switch (context.Response.StatusCode)
        {
            case StatusCodes.Status401Unauthorized:
            {
                var problemDetails = problemDetailsCreator.Unauthorized();
                await _WriteAsync(context, problemDetails).ConfigureAwait(false);

                break;
            }
            case StatusCodes.Status403Forbidden:
            {
                // TenantRequirementHandler stashes this marker when it fails the request, so the
                // bare 403 produced by ASP.NET Core's default IAuthorizationMiddlewareResultHandler
                // can be enriched with the structured g:tenant_required discriminator here — no
                // dependency on the consumer's IAuthorizationMiddlewareResultHandler registration
                // order.
                if (hasTenantFeature)
                {
                    context.Response.Clear();
                    // Clear() resets StatusCode to 200; restore before writing.
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                }

                var problemDetails = hasTenantFeature
                    ? problemDetailsCreator.Forbidden(
                        detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                        error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
                    )
                    : problemDetailsCreator.Forbidden();
                await _WriteAsync(context, problemDetails).ConfigureAwait(false);

                break;
            }
            case StatusCodes.Status404NotFound:
            {
                var problemDetails = problemDetailsCreator.EndpointNotFound();
                await _WriteAsync(context, problemDetails).ConfigureAwait(false);

                break;
            }
        }
    }

    // Routes writes through IProblemDetailsService so consumer CustomizeProblemDetails hooks run.
    // Falls back to Results.Problem when the service is not registered or declines to write
    // (TryWriteAsync returns false), ensuring structured output even in minimal-host scenarios.
    private static async Task _WriteAsync(HttpContext context, ProblemDetails problemDetails)
    {
        var service = context.RequestServices.GetService<IProblemDetailsService>();

        if (service is not null)
        {
            var written = await service
                .TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problemDetails })
                .ConfigureAwait(false);

            if (written)
            {
                return;
            }
        }

        await Results.Problem(problemDetails).ExecuteAsync(context).ConfigureAwait(false);
    }
}
