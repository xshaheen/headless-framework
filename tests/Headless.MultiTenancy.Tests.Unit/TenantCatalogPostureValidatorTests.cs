// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    private static HeadlessTenancyValidationContext _CreateContext(TenantPostureManifest manifest)
    {
        var services = new ServiceCollection().BuildServiceProvider();

        return new HeadlessTenancyValidationContext(services, manifest);
    }
}
