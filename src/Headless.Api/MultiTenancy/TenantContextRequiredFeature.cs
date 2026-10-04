// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Rejection set by <c>TenantRequirementHandler</c> when authorization fails because no tenant is
/// resolved. On a 403 it replaces the generic Forbidden body with the structured
/// <c>g:tenant_required</c> ProblemDetails, overriding any partial response an upstream
/// <c>IAuthorizationMiddlewareResultHandler</c> began; any other status is declined, because the
/// discriminator describes an authorization failure only.
/// </summary>
/// <remarks>
/// Set only on HTTP authorization contexts (where <c>AuthorizationHandlerContext.Resource</c> is
/// an <c>HttpContext</c>). For non-HTTP transports (SignalR, gRPC, programmatic
/// <c>IAuthorizationService.AuthorizeAsync</c>), consumers should inspect
/// <c>failure.FailedRequirements.OfType&lt;TenantRequirement&gt;()</c> directly — the
/// <c>g:tenant_required</c> discriminator is HTTP-pipeline-only.
/// </remarks>
internal sealed class TenantContextRequiredFeature : IStatusCodeRejectionFeature
{
    public static TenantContextRequiredFeature Instance { get; } = new();

    private TenantContextRequiredFeature() { }

    public async Task<bool> TryWriteResponseAsync(HttpContext context)
    {
        if (context.Response.StatusCode != StatusCodes.Status403Forbidden)
        {
            return false;
        }

        // Clear() also resets the status to 200; WriteAsync assigns the 403 again before writing.
        context.Response.Clear();

        var problemDetails = context
            .RequestServices.GetRequiredService<IProblemDetailsCreator>()
            .Forbidden(
                detail: HeadlessProblemDetailsConstants.Details.TenantContextRequired,
                error: HeadlessProblemDetailsConstants.Errors.TenantContextRequired
            );

        await TenantCatalogRejectionWriter
            .WriteAsync(context, StatusCodes.Status403Forbidden, problemDetails)
            .ConfigureAwait(false);

        return true;
    }
}
