// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class TenantCatalogPostureValidatorTests
{
    private readonly TenantCatalogPostureValidator _sut = new();

    [Fact]
    public void should_report_nothing_when_catalog_seam_is_not_configured()
    {
        // given — the catalog is opt-in (R5)
        var context = _CreateContext(new TenantPostureManifest());

        // when
        var diagnostics = _sut.Validate(context);

        // then
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void should_report_nothing_for_accessor_only_posture()
    {
        // given — R18's explicit accessor-only carve-out: store configured, no resolution
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Configured,
            TenantCatalogPosture.AccessorCapability
        );
        var context = _CreateContext(manifest);

        // when
        var diagnostics = _sut.Validate(context);

        // then
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void should_report_error_when_resolution_is_recorded_without_a_store()
    {
        // given — resolution capability with no accessor capability means no store was configured
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.ResolutionCapability
        );
        var context = _CreateContext(manifest);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        diagnostics
            .Should()
            .ContainSingle(diagnostic =>
                diagnostic.Code == "CATALOG_RESOLUTION_WITHOUT_STORE"
                && diagnostic.Severity == HeadlessTenancyDiagnosticSeverity.Error
            );
    }

    [Fact]
    public void should_report_error_when_resolution_is_recorded_without_the_pipeline_runtime_marker()
    {
        // given — both capabilities present, but the pipeline never marked itself active
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.AccessorCapability,
            TenantCatalogPosture.ResolutionCapability
        );
        var context = _CreateContext(manifest);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "CATALOG_RESOLUTION_WITHOUT_PIPELINE");
    }

    [Fact]
    public void should_report_nothing_when_resolution_has_both_a_store_and_a_registered_pipeline()
    {
        // given
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.AccessorCapability,
            TenantCatalogPosture.ResolutionCapability
        );
        manifest.MarkRuntimeApplied(TenantCatalogPosture.Seam, TenantCatalogPosture.ResolutionPipelineRuntimeMarker);
        var context = _CreateContext(manifest);

        // when
        var diagnostics = _sut.Validate(context);

        // then
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void should_report_error_when_an_accessor_only_catalog_has_no_caching_provider()
    {
        // given — a store is configured, but the host never called AddHeadlessCaching(...); TenantCatalogService
        // still takes two ICache<T> dependencies, so even ICurrentTenantInfo reads would throw at runtime
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Configured,
            TenantCatalogPosture.AccessorCapability
        );
        var context = _CreateContext(manifest, withCachingProvider: false);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        var diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("CATALOG_WITHOUT_CACHING_PROVIDER");
        diagnostic.Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostic.Message.Should().Contain("AddHeadlessCaching(...)");
    }

    [Fact]
    public void should_report_error_when_a_resolving_catalog_has_no_caching_provider()
    {
        // given — a fully wired resolution posture whose only defect is the missing caching provider
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.AccessorCapability,
            TenantCatalogPosture.ResolutionCapability
        );
        manifest.MarkRuntimeApplied(TenantCatalogPosture.Seam, TenantCatalogPosture.ResolutionPipelineRuntimeMarker);
        var context = _CreateContext(manifest, withCachingProvider: false);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "CATALOG_WITHOUT_CACHING_PROVIDER");
    }

    [Fact]
    public void should_report_nothing_about_caching_when_a_caching_provider_is_registered()
    {
        // given — AddHeadlessCaching(...) supplies the open-generic ICache<> the catalog service needs
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Configured,
            TenantCatalogPosture.AccessorCapability
        );
        var context = _CreateContext(manifest);

        // when
        var diagnostics = _sut.Validate(context);

        // then
        diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "CATALOG_WITHOUT_CACHING_PROVIDER");
    }

    [Fact]
    public void should_report_nothing_about_caching_when_no_store_is_configured()
    {
        // given — resolution recorded without a store: TenantCatalogService was never registered, so the
        // missing caching provider is not this posture's defect
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.ResolutionCapability
        );
        var context = _CreateContext(manifest, withCachingProvider: false);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "CATALOG_WITHOUT_CACHING_PROVIDER");
    }

    [Fact]
    public void should_report_error_when_resolution_is_recorded_without_the_status_codes_rewriter_marker()
    {
        // given — a fully wired resolution posture whose only defect is that UseStatusCodesRewriter() was
        // never called, leaving the R19 authorization-tier rejection distinguishable from an unknown tenant
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.AccessorCapability,
            TenantCatalogPosture.ResolutionCapability
        );
        manifest.MarkRuntimeApplied(TenantCatalogPosture.Seam, TenantCatalogPosture.ResolutionPipelineRuntimeMarker);
        var context = _CreateContext(manifest, withStatusCodesRewriter: false);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        var diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be("CATALOG_RESOLUTION_WITHOUT_REWRITER");
        diagnostic.Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostic.Message.Should().Contain("UseStatusCodesRewriter()").And.Contain("enumerate tenants");
    }

    [Fact]
    public void should_report_nothing_about_the_rewriter_when_its_marker_is_present()
    {
        // given
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.AccessorCapability,
            TenantCatalogPosture.ResolutionCapability
        );
        manifest.MarkRuntimeApplied(TenantCatalogPosture.Seam, TenantCatalogPosture.ResolutionPipelineRuntimeMarker);
        var context = _CreateContext(manifest);

        // when
        var diagnostics = _sut.Validate(context);

        // then
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void should_report_nothing_about_the_rewriter_for_an_accessor_only_host()
    {
        // given — deliberately a different gate from the caching rule: tier-2 R19 only exists for
        // identifier-resolved requests, so an accessor-only host has no mismatch path to collapse
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Configured,
            TenantCatalogPosture.AccessorCapability
        );
        var context = _CreateContext(manifest, withStatusCodesRewriter: false);

        // when
        var diagnostics = _sut.Validate(context).ToArray();

        // then
        diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// Registers a caching provider and marks the status-codes rewriter by default so the tests above
    /// isolate the posture rules they target; pass <see langword="false"/> to exercise the missing-provider
    /// probe or the missing-rewriter rule.
    /// </summary>
    private static HeadlessTenancyValidationContext _CreateContext(
        TenantPostureManifest manifest,
        bool withCachingProvider = true,
        bool withStatusCodesRewriter = true
    )
    {
        var services = new ServiceCollection();

        if (withCachingProvider)
        {
            services.AddHeadlessCaching(setup => setup.UseInMemory());
        }

        // Skipped when no seam was recorded: MarkRuntimeApplied would otherwise materialize a Catalog seam
        // and rob the "catalog not configured at all" case of the posture it is asserting.
        if (withStatusCodesRewriter && manifest.GetSeam(TenantCatalogPosture.Seam) is not null)
        {
            manifest.MarkRuntimeApplied(
                TenantCatalogPosture.Seam,
                TenantCatalogPosture.StatusCodesRewriterRuntimeMarker
            );
        }

        return new HeadlessTenancyValidationContext(services.BuildServiceProvider(), manifest);
    }
}
