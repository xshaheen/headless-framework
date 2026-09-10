// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Shared, non-PII posture identifiers for the <c>Catalog</c> tenancy seam. <c>Headless.MultiTenancy</c>
/// records <see cref="AccessorCapability"/> when a store is configured via <c>Catalog(...)</c>.
/// <c>Headless.Api.Core</c>'s pre-auth resolution middleware (U5) is the intended caller that records
/// <see cref="ResolutionCapability"/> and marks <see cref="ResolutionPipelineRuntimeMarker"/> once an
/// identifier source is registered, and its <c>UseStatusCodesRewriter()</c> marks
/// <see cref="StatusCodesRewriterRuntimeMarker"/> — <see cref="TenantCatalogPostureValidator"/>
/// cross-checks both packages' contributions against these shared string constants so a typo in either
/// package fails loudly instead of silently producing an unchecked posture.
/// </summary>
[PublicAPI]
public static class TenantCatalogPosture
{
    /// <summary>The tenancy posture seam name this catalog contributes to.</summary>
    public const string Seam = "Catalog";

    /// <summary>
    /// Capability label recorded when a tenant store is configured: <see cref="ICurrentTenantInfo"/>
    /// metadata reads are available, independent of whether identifier-based resolution is active.
    /// </summary>
    public const string AccessorCapability = "catalog-accessor";

    /// <summary>
    /// Capability label recorded when identifier-based resolution is active (the pre-auth HTTP
    /// middleware, or an equivalent identifier-source caller).
    /// </summary>
    public const string ResolutionCapability = "catalog-resolution";

    /// <summary>
    /// Runtime marker a resolution-capable seam records once at least one identifier source is
    /// registered and the resolution pipeline hook is wired. Its absence alongside
    /// <see cref="ResolutionCapability"/> means resolution was declared active but nothing will ever
    /// call it — a startup-blocking misconfiguration (R18).
    /// </summary>
    public const string ResolutionPipelineRuntimeMarker = "IdentifierResolutionPipelineActive";

    /// <summary>
    /// Runtime marker recorded when the HTTP seam's status-codes rewriter middleware is added to the
    /// pipeline. The R19 mapping-integrity check's authorization tier only fails the evaluation and
    /// marks the request; the generic tenant rejection that keeps a mismatch indistinguishable from an
    /// unknown identifier is written by that middleware. Its absence alongside
    /// <see cref="ResolutionCapability"/> therefore leaves a tenant-enumeration oracle open (R11).
    /// </summary>
    public const string StatusCodesRewriterRuntimeMarker = "StatusCodesRewriterActive";
}

/// <summary>
/// Validates that the <see cref="TenantCatalogPosture.Seam"/> posture is internally consistent (R18):
/// resolution-capable without a configured store, without a registered pipeline, or without the
/// status-codes rewriter that writes the R19 rejection are all startup-blocking. Accessor-only posture
/// (store configured, no resolution) is otherwise valid and never flagged — R18's explicit
/// accessor-only carve-out.
/// <para>
/// The catalog's other hard prerequisite — a caching provider behind the read-through caches — is not
/// checked here. It is declared with <c>RequireRegisteredService</c> in <c>Catalog(...)</c> and enforced
/// by the shared <c>Headless.Hosting</c> startup check, which every cache-consuming Headless feature uses,
/// so a host missing the provider entirely gets one message naming all affected features.
/// </para>
/// </summary>
internal sealed class TenantCatalogPostureValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        var seam = context.Manifest.GetSeam(TenantCatalogPosture.Seam);

        if (seam is null)
        {
            // The catalog is opt-in (R5): no seam recorded at all means nothing to validate.
            yield break;
        }

        var hasAccessor = seam.Capabilities.Contains(TenantCatalogPosture.AccessorCapability, StringComparer.Ordinal);
        var hasResolution = seam.Capabilities.Contains(
            TenantCatalogPosture.ResolutionCapability,
            StringComparer.Ordinal
        );

        if (!hasResolution)
        {
            // Accessor-only (or a seam with neither label, which is not this validator's concern) —
            // valid posture per R18; never fails.
            yield break;
        }

        if (!hasAccessor)
        {
            yield return HeadlessTenancyDiagnostic.Error(
                TenantCatalogPosture.Seam,
                "CATALOG_RESOLUTION_WITHOUT_STORE",
                "Tenant catalog identifier resolution is enabled but no tenant store is configured. "
                    + "Call HeadlessTenancyBuilder.Catalog(catalog => catalog.UseInMemory(...) / .UseConfiguration(...) / .UseEntityFramework<T>()) "
                    + "or disable resolution."
            );
        }

        if (!seam.RuntimeMarkers.Contains(TenantCatalogPosture.ResolutionPipelineRuntimeMarker, StringComparer.Ordinal))
        {
            yield return HeadlessTenancyDiagnostic.Error(
                TenantCatalogPosture.Seam,
                "CATALOG_RESOLUTION_WITHOUT_PIPELINE",
                "Tenant catalog identifier resolution is enabled but no identifier source or resolution "
                    + "pipeline hook is registered, so it will never run. Register an identifier source "
                    + "before enabling resolution, or disable it."
            );
        }

        // Gated on resolution rather than on the accessor capability: the rewriter only matters for
        // identifier-resolved requests, since R19's authorization tier exists only for them. An
        // accessor-only host has no mismatch path and needs nothing here.
        if (
            !seam.RuntimeMarkers.Contains(TenantCatalogPosture.StatusCodesRewriterRuntimeMarker, StringComparer.Ordinal)
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                TenantCatalogPosture.Seam,
                "CATALOG_RESOLUTION_WITHOUT_REWRITER",
                "Tenant catalog identifier resolution is enabled but UseStatusCodesRewriter() was never called. "
                    + "The R19 mapping-integrity check's authorization tier only fails the evaluation and marks "
                    + "the request; StatusCodesRewriterMiddleware is what turns that into the generic tenant "
                    + "rejection. Without it a mismatch surfaces as a bare authorization failure while an unknown "
                    + "identifier surfaces as the 404 rejection, so a caller can tell an existing tenant from an "
                    + "unknown one by status code alone and enumerate tenants. Call UseStatusCodesRewriter() "
                    + "(or UseHeadless() from Headless.Api.ServiceDefaults) so that it wraps UseAuthorization()."
            );
        }
    }
}
