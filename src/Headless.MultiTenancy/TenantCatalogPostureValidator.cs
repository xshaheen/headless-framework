// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Microsoft.Extensions.DependencyInjection;

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
/// are both startup-blocking, as is a configured store with no caching provider to back the catalog's
/// read-through caches. Accessor-only posture (store configured, no resolution) is otherwise valid and
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

        // Gated on the accessor capability rather than on resolution: that label is recorded exactly when
        // Catalog(...) configured a store, which is exactly when TenantCatalogService — and its two
        // ICache<T> constructor dependencies — is registered. Accessor-only hosts need the caches just as
        // much, because ICurrentTenantInfo reads go through the same service. Headless.MultiTenancy
        // references only Headless.Caching.Abstractions, so the open-generic ICache<> implementation comes
        // from a caching provider package; without one the host starts clean and every catalog read throws.
        if (hasAccessor && !_HasCachingProvider(context.Services))
        {
            yield return HeadlessTenancyDiagnostic.Error(
                TenantCatalogPosture.Seam,
                "CATALOG_WITHOUT_CACHING_PROVIDER",
                "A tenant catalog store is configured but no caching provider is registered, so the catalog's "
                    + "read-through caches cannot be resolved and every tenant lookup would fail at runtime. "
                    + "Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."
            );
        }

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

    /// <summary>
    /// Reports whether a closed <see cref="ICache{T}"/> is registered, asking
    /// <see cref="IServiceProviderIsService"/> in preference to actually resolving one: every provider
    /// registers the open-generic <c>ICache&lt;&gt;</c> over the root <c>ICache</c> singleton, so a resolve
    /// here would build the backing cache during startup validation (for Redis, reaching into its connection
    /// options) and would turn a caching misconfiguration into this validator's synthetic
    /// <c>VALIDATOR_THREW</c> diagnostic instead of the actionable one above. Falls back to a null-returning
    /// resolve for a container that does not expose the probe.
    /// </summary>
    private static bool _HasCachingProvider(IServiceProvider services)
    {
        var probe = services.GetService<IServiceProviderIsService>();

        return probe is not null
            ? probe.IsService(typeof(ICache<TenantIdentifierCacheItem>))
            : services.GetService<ICache<TenantIdentifierCacheItem>>() is not null;
    }
}
