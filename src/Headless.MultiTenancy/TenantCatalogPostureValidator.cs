// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Shared, non-PII posture identifiers for the <c>Catalog</c> tenancy seam. <c>Headless.MultiTenancy</c>
/// records <see cref="AccessorCapability"/> when a store is configured via <c>Catalog(...)</c>.
/// <c>Headless.Api.Core</c>'s pre-auth resolution middleware (U5) is the intended caller that records
/// <see cref="ResolutionCapability"/> and marks <see cref="ResolutionPipelineRuntimeMarker"/> once an
/// identifier source is registered — <see cref="TenantCatalogPostureValidator"/> cross-checks both
/// packages' contributions against these shared string constants so a typo in either package fails
/// loudly instead of silently producing an unchecked posture.
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
}

/// <summary>
/// Validates that the <see cref="TenantCatalogPosture.Seam"/> posture is internally consistent (R18):
/// resolution-capable without a configured store, or resolution-capable without a registered pipeline,
/// are both startup-blocking. Accessor-only posture (store configured, no resolution) is valid and
/// never flagged — R18's explicit accessor-only carve-out.
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
    }
}
