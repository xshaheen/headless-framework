// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Per-request feature set by <c>TenantIdentifierIntegrityHandler</c> when an identifier-resolved
/// canonical tenant id does not match the authenticated principal's tenant claim (R19).
/// <c>StatusCodesRewriterMiddleware</c> reads this feature on a bare 403 response to substitute the
/// R11 mismatch ProblemDetails body — generic (404, <c>g:tenant_resolution_failed</c>) by default, or
/// granular (403, <c>g:tenant_identifier_mismatch</c>) when <c>TenantCatalogOptions.DetailedResolutionErrors</c>
/// is enabled. Typed features are keyed by .NET type, so this is not subject to the string-key
/// collision risk of <c>HttpContext.Items</c>.
/// </summary>
internal sealed class TenantIdentifierMismatchFeature
{
    public static TenantIdentifierMismatchFeature Instance { get; } = new();

    private TenantIdentifierMismatchFeature() { }
}
