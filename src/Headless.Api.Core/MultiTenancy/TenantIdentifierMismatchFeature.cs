// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Rejection set by <c>TenantIdentifierIntegrityHandler</c> when an identifier-resolved canonical tenant
/// id does not match the authenticated principal's tenant claim. It writes the mismatch ProblemDetails —
/// generic (404, <c>g:tenant_resolution_failed</c>) by default, or granular (403,
/// <c>g:tenant_identifier_mismatch</c>) when <see cref="TenantCatalogOptions.DetailedResolutionErrors"/>
/// is enabled — whatever status the authorization pipeline produced.
/// </summary>
/// <remarks>
/// It never declines and overrides both status and body: a cookie-style scheme forbids with a 302, and
/// any surviving difference from the generic unknown/disabled rejection is exactly the enumeration signal
/// the secure-by-default rejection is designed to remove. For the same reason it is the one rejection
/// that replaces an already-set <see cref="IStatusCodeRejectionFeature"/> instead of deferring to it.
/// </remarks>
internal sealed class TenantIdentifierMismatchFeature : IStatusCodeRejectionFeature
{
    private static readonly TenantIdentifierMismatchFeature _Generic = new(detailed: false);
    private static readonly TenantIdentifierMismatchFeature _Detailed = new(detailed: true);

    private TenantIdentifierMismatchFeature(bool detailed)
    {
        Detailed = detailed;
    }

    /// <summary>Whether the granular <c>g:tenant_identifier_mismatch</c> rejection is written.</summary>
    public bool Detailed { get; }

    public static TenantIdentifierMismatchFeature For(bool detailed) => detailed ? _Detailed : _Generic;

    public async Task<bool> TryWriteResponseAsync(HttpContext context)
    {
        context.Response.Clear();

        // RejectMismatchAsync also stamps Cache-Control: no-store — the same rejection path
        // TenantResolutionMiddleware's claim-vs-feature fast path takes, so both mismatch rewrites stay
        // byte-identical, headers included.
        await TenantCatalogRejectionWriter
            .RejectMismatchAsync(
                context,
                context.RequestServices.GetRequiredService<IProblemDetailsCreator>(),
                Detailed
            )
            .ConfigureAwait(false);

        return true;
    }
}
