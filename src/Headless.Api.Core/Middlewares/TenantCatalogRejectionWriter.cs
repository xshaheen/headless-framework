// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.MultiTenancy;
using Headless.MultiTenancy.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Middlewares;

/// <summary>
/// Maps tenant-catalog resolution outcomes to <see cref="ProblemDetails"/> per R11/KTD9 (secure-by-default
/// collapse of unknown/disabled/mismatch into one generic rejection; granular codes only under
/// <see cref="TenantCatalogOptions.DetailedResolutionErrors"/>) and writes them through the same
/// <see cref="IProblemDetailsService"/> + <c>Results.Problem</c> fallback precedent as
/// <c>StatusCodesRewriterMiddleware</c>. Shared by <c>TenantCatalogResolutionMiddleware</c> (pre-auth
/// unknown/disabled/invalid), <c>TenantResolutionMiddleware</c>'s claim-vs-feature fast path, and
/// <c>StatusCodesRewriterMiddleware</c>'s post-authorization R19 mismatch rewrite.
/// </summary>
internal static class TenantCatalogRejectionWriter
{
    /// <summary>Builds and writes the ProblemDetails response for a non-<see cref="TenantResolutionKind.Resolved"/> outcome.</summary>
    public static Task RejectAsync(
        HttpContext context,
        TenantResolutionKind kind,
        IProblemDetailsCreator problemDetailsCreator,
        bool detailed
    )
    {
        var (statusCode, problemDetails) = BuildOutcome(kind, problemDetailsCreator, detailed);

        return WriteAsync(context, statusCode, problemDetails);
    }

    /// <summary>Builds and writes the ProblemDetails response for an R19 identifier/claim mismatch.</summary>
    public static Task RejectMismatchAsync(
        HttpContext context,
        IProblemDetailsCreator problemDetailsCreator,
        bool detailed
    )
    {
        var (statusCode, problemDetails) = BuildMismatch(problemDetailsCreator, detailed);

        return WriteAsync(context, statusCode, problemDetails);
    }

    /// <summary>Maps <see cref="TenantResolutionKind.Unknown"/>, <see cref="TenantResolutionKind.Disabled"/>, and <see cref="TenantResolutionKind.Invalid"/> per R11/KTD9.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is <see cref="TenantResolutionKind.Resolved"/> or <see cref="TenantResolutionKind.Ignored"/> — neither is a rejection outcome.</exception>
    public static (int StatusCode, ProblemDetails ProblemDetails) BuildOutcome(
        TenantResolutionKind kind,
        IProblemDetailsCreator problemDetailsCreator,
        bool detailed
    )
    {
        return kind switch
        {
            // Shape validation reveals nothing tenant-specific — always its own code/status regardless
            // of the diagnostics option (R11).
            TenantResolutionKind.Invalid => (
                StatusCodes.Status400BadRequest,
                problemDetailsCreator.BadRequest(error: TenancyMessageDescriber.IdentifierInvalid())
            ),
            TenantResolutionKind.Unknown when detailed => (
                StatusCodes.Status404NotFound,
                problemDetailsCreator.EntityNotFound(TenancyMessageDescriber.Unknown())
            ),
            TenantResolutionKind.Disabled when detailed => (
                StatusCodes.Status403Forbidden,
                problemDetailsCreator.Forbidden(error: TenancyMessageDescriber.Disabled())
            ),
            // Secure-by-default collapse: unknown and disabled are byte-identical to each other (and to
            // the R19 mismatch rejection built by BuildMismatch) so a caller cannot enumerate tenants or
            // their status from response differences.
            TenantResolutionKind.Unknown or TenantResolutionKind.Disabled => (
                StatusCodes.Status404NotFound,
                problemDetailsCreator.EntityNotFound(TenancyMessageDescriber.ResolutionFailed())
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a rejection outcome."),
        };
    }

    /// <summary>Maps the R19 identifier/claim mismatch outcome per R11/KTD9.</summary>
    public static (int StatusCode, ProblemDetails ProblemDetails) BuildMismatch(
        IProblemDetailsCreator problemDetailsCreator,
        bool detailed
    )
    {
        return detailed
            ? (
                StatusCodes.Status403Forbidden,
                problemDetailsCreator.Forbidden(error: TenancyMessageDescriber.IdentifierMismatch())
            )
            : (
                StatusCodes.Status404NotFound,
                problemDetailsCreator.EntityNotFound(TenancyMessageDescriber.ResolutionFailed())
            );
    }

    /// <summary>Writes <paramref name="problemDetails"/> through <see cref="IProblemDetailsService"/>, falling back to <see cref="Results.Problem(ProblemDetails)"/>.</summary>
    public static async Task WriteAsync(HttpContext context, int statusCode, ProblemDetails problemDetails)
    {
        context.Response.StatusCode = statusCode;

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
