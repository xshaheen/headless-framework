// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.MultiTenancy;

namespace Tests;

public sealed class TenantPostureManifestTests
{
    [Fact]
    public void should_store_undefined_status_on_first_record_then_throw_on_next_record()
    {
        // Characterization pin of the documented asymmetry: status is validated only when merged
        // with an existing seam (see RecordSeam XML docs). The first record of an undefined value
        // is stored silently; the *next* record on the same seam throws far from the original cause.
        var manifest = new TenantPostureManifest();

        manifest.RecordSeam("Seam", (TenantPostureStatus)99);
        manifest.GetSeam("Seam")!.Status.Should().Be((TenantPostureStatus)99);

        var act = () => manifest.RecordSeam("Seam", TenantPostureStatus.Configured);
        act.Should().Throw<InvalidEnumArgumentException>();
    }

    [Fact]
    public void should_drop_blank_capability_labels_and_deduplicate_within_one_record()
    {
        var manifest = new TenantPostureManifest();

        manifest.RecordSeam("Seam", TenantPostureStatus.Configured, "a", " ", "", null!, "a");

        manifest.GetSeam("Seam")!.Capabilities.Should().Equal("a");
    }

    [Fact]
    public void should_not_duplicate_capabilities_when_same_label_recorded_across_calls()
    {
        var manifest = new TenantPostureManifest();

        manifest.RecordSeam("Seam", TenantPostureStatus.Configured, "propagate-tenant");
        manifest.RecordSeam("Seam", TenantPostureStatus.Configured, "propagate-tenant");

        manifest.GetSeam("Seam")!.Capabilities.Should().Equal("propagate-tenant");
    }

    [Fact]
    public void should_not_duplicate_runtime_markers_when_same_marker_applied_twice()
    {
        var manifest = new TenantPostureManifest();

        manifest.MarkRuntimeApplied("Seam", "UseHeadlessTenancy");
        manifest.MarkRuntimeApplied("Seam", "UseHeadlessTenancy");

        manifest.GetSeam("Seam")!.RuntimeMarkers.Should().Equal("UseHeadlessTenancy");
    }

    [Fact]
    public void should_throw_when_capabilities_array_is_null()
    {
        var manifest = new TenantPostureManifest();

        var act = () => manifest.RecordSeam("Seam", TenantPostureStatus.Configured, capabilities: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_return_decoupled_snapshot_when_seams_accessed_before_later_writes()
    {
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam("First", TenantPostureStatus.Configured);

        var snapshot = manifest.Seams;
        manifest.RecordSeam("Second", TenantPostureStatus.Enforcing);
        manifest.MarkRuntimeApplied("First", "late-marker");

        snapshot.Should().ContainSingle().Which.Seam.Should().Be("First");
        snapshot.Single().RuntimeMarkers.Should().BeEmpty();
    }

    [Fact]
    public async Task should_record_all_contributions_when_one_seam_written_concurrently()
    {
        // The manifest's headline contract is thread-safe writes from multiple package seams.
        var manifest = new TenantPostureManifest();
        const int contributions = 100;

        await Parallel.ForAsync(
            0,
            contributions,
            TestContext.Current.CancellationToken,
            (i, _) =>
            {
                manifest.RecordSeam("Seam", (TenantPostureStatus)(i % 4), $"capability-{i}");
                manifest.MarkRuntimeApplied("Seam", $"marker-{i}");
                return ValueTask.CompletedTask;
            }
        );

        var seam = manifest.GetSeam("Seam");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should().HaveCount(contributions);
        seam.RuntimeMarkers.Should().HaveCount(contributions);
    }

    [Fact]
    public void should_report_unknown_seam_as_not_configured_without_throwing()
    {
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam("Known", TenantPostureStatus.Configured);

        manifest.GetSeam("Unknown").Should().BeNull();
        manifest.IsConfigured("Unknown").Should().BeFalse();
        manifest.IsConfigured("Known").Should().BeTrue();
        manifest.HasRuntimeMarker("Unknown", "marker").Should().BeFalse();
    }

    [Fact]
    public void should_compare_runtime_markers_with_ordinal_case_sensitivity()
    {
        var manifest = new TenantPostureManifest();
        manifest.MarkRuntimeApplied("Seam", "Marker");
        manifest.MarkRuntimeApplied("Other", "elsewhere");

        manifest.HasRuntimeMarker("Seam", "Marker").Should().BeTrue();
        manifest.HasRuntimeMarker("Seam", "marker").Should().BeFalse();
        manifest.HasRuntimeMarker("Seam", "elsewhere").Should().BeFalse();
    }

    [Fact]
    public void should_validate_marker_argument_even_when_seam_does_not_exist()
    {
        var manifest = new TenantPostureManifest();

        var act = () => manifest.HasRuntimeMarker("Unknown", " ");

        act.Should().Throw<ArgumentException>();
    }
}
