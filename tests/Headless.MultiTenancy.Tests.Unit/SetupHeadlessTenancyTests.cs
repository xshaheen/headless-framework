// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class SetupHeadlessTenancyTests
{
    [Fact]
    public void should_register_manifest_and_startup_validator_once()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(_ => { });
        builder.AddHeadlessTenancy(_ => { });

        // then
        builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest))
            .Should()
            .ContainSingle();
        builder.Services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType?.Name == "HeadlessTenancyStartupValidator"
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void should_record_seam_posture_idempotently()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.RecordSeam("Messaging", TenantPostureStatus.Propagating, "propagate-tenant");
            tenancy.RecordSeam("Messaging", TenantPostureStatus.Propagating, "require-tenant-on-publish");
        });

        var manifest = builder.Services.GetOrAddTenantPostureManifest();

        // then
        var seam = manifest.GetSeam("Messaging");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Propagating);
        seam.Capabilities.Should().BeEquivalentTo("propagate-tenant", "require-tenant-on-publish");
    }

    [Fact]
    public async Task should_throw_startup_diagnostic_when_validator_returns_error()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(new TestValidator());
        builder.AddHeadlessTenancy(tenancy => tenancy.RecordSeam("Http", TenantPostureStatus.Configured));

        using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)provider
            .GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "HeadlessTenancyStartupValidator");

        // when — validation runs in StartingAsync so it fires before any other hosted service's StartAsync.
        Func<Task> act = () => hostedService.StartingAsync(CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*HEADLESS_TEST*")
            .WithMessage("*Http seam is missing runtime marker*");
    }

    [Fact]
    public async Task should_throw_all_startup_diagnostics_when_multiple_validators_return_errors()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(
            new TestValidator("HEADLESS_TEST_ONE", "First seam failed.")
        );
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(
            new TestValidator("HEADLESS_TEST_TWO", "Second seam failed.")
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.RecordSeam("Http", TenantPostureStatus.Configured));

        using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)provider
            .GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "HeadlessTenancyStartupValidator");

        // when
        Func<Task> act = () => hostedService.StartingAsync(CancellationToken.None);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*HEADLESS_TEST_ONE*")
            .WithMessage("*First seam failed*")
            .WithMessage("*HEADLESS_TEST_TWO*")
            .WithMessage("*Second seam failed*");
    }

    [Fact]
    public void should_not_include_tenant_values_in_manifest()
    {
        // given — sentinel tenant IDs that must never appear inside the manifest snapshot
        const string sentinelTenantA = "tenant-a-secret-12345";
        const string sentinelTenantB = "tenant-b-secret-67890";
        const string sentinelClaimValue = "claim-value-pii";

        var manifest = new TenantPostureManifest();

        // when — record seams with non-PII labels only; the manifest must not leak tenant values
        manifest.RecordSeam("Http", TenantPostureStatus.Configured, "resolve-from-claims");
        manifest.MarkRuntimeApplied("Http", "UseHeadlessTenancy");
        manifest.RecordSeam("Messaging", TenantPostureStatus.Propagating, "propagate-tenant");

        // then
        var seam = manifest.GetSeam("Http");
        seam.Should().NotBeNull();
        seam!.Capabilities.Should().BeEquivalentTo("resolve-from-claims");
        seam.RuntimeMarkers.Should().BeEquivalentTo("UseHeadlessTenancy");

        // Strengthened: explicitly assert no nested string property contains any tenant identifier.
        var allStrings = manifest
            .Seams.SelectMany(s => new[] { s.Seam }.Concat(s.Capabilities).Concat(s.RuntimeMarkers))
            .ToArray();
        allStrings.Should().NotContain(sentinelTenantA).And.NotContain(sentinelTenantB).And.NotContain(sentinelClaimValue);
        allStrings.Should().AllSatisfy(value =>
        {
            value.Should().NotContain("tenant-a", because: "manifest must not record tenant IDs");
            value.Should().NotContain("tenant-b", because: "manifest must not record tenant IDs");
        });
    }

    [Fact]
    public void should_keep_higher_priority_status_when_record_seam_called_in_reverse_order()
    {
        // given — record Enforcing first, then Propagating; precedence must keep Enforcing.
        var manifest = new TenantPostureManifest();

        // when
        manifest.RecordSeam("Messaging", TenantPostureStatus.Enforcing, "require-tenant-on-publish");
        manifest.RecordSeam("Messaging", TenantPostureStatus.Propagating, "propagate-tenant");

        // then
        var seam = manifest.GetSeam("Messaging");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should().BeEquivalentTo("require-tenant-on-publish", "propagate-tenant");
    }

    [Fact]
    public void should_upgrade_status_when_record_seam_called_with_higher_priority_status()
    {
        // given — record Configured first, then Guarded; precedence must keep Guarded.
        var manifest = new TenantPostureManifest();

        // when
        manifest.RecordSeam("EntityFramework", TenantPostureStatus.Configured, "ef-baseline");
        manifest.RecordSeam("EntityFramework", TenantPostureStatus.Guarded, "guard-tenant-writes");

        // then
        var seam = manifest.GetSeam("EntityFramework");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Guarded);
    }

    [Fact]
    public void should_mark_runtime_applied_when_seam_not_previously_recorded()
    {
        // given — fresh manifest with no prior RecordSeam call
        var manifest = new TenantPostureManifest();

        // when
        manifest.MarkRuntimeApplied("some-seam", "some-marker");

        // then — the seam should now exist with Configured status and the runtime marker
        var seam = manifest.GetSeam("some-seam");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Configured);
        seam.RuntimeMarkers.Should().BeEquivalentTo("some-marker");
        seam.Capabilities.Should().BeEmpty();
    }

    private sealed class TestValidator(
        string code = "HEADLESS_TEST",
        string message = "Http seam is missing runtime marker."
    ) : IHeadlessTenancyValidator
    {
        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            yield return HeadlessTenancyDiagnostic.Error("Http", code, message);
        }
    }
}
