// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class HeadlessTenancySetupTests
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
            tenancy.RecordSeam("Messaging", TenantPostureStatuses.Propagating, "propagate-tenant");
            tenancy.RecordSeam("Messaging", TenantPostureStatuses.Propagating, "require-tenant-on-publish");
        });

        var manifest = builder.Services.GetOrAddTenantPostureManifest();

        // then
        var seam = manifest.GetSeam("Messaging");
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatuses.Propagating);
        seam.Capabilities.Should().BeEquivalentTo("propagate-tenant", "require-tenant-on-publish");
    }

    [Fact]
    public async Task should_throw_startup_diagnostic_when_validator_returns_error()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(new TestValidator());
        builder.AddHeadlessTenancy(tenancy => tenancy.RecordSeam("Http", TenantPostureStatuses.Configured));

        using var provider = builder.Services.BuildServiceProvider();
        var hostedService = provider
            .GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "HeadlessTenancyStartupValidator");

        // when
        Func<Task> act = () => hostedService.StartAsync(CancellationToken.None);

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
        builder.AddHeadlessTenancy(tenancy => tenancy.RecordSeam("Http", TenantPostureStatuses.Configured));

        using var provider = builder.Services.BuildServiceProvider();
        var hostedService = provider
            .GetServices<IHostedService>()
            .Single(service => service.GetType().Name == "HeadlessTenancyStartupValidator");

        // when
        Func<Task> act = () => hostedService.StartAsync(CancellationToken.None);

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
        // given
        var manifest = new TenantPostureManifest();

        // when
        manifest.RecordSeam("Http", TenantPostureStatuses.Configured, "resolve-from-claims");
        manifest.MarkRuntimeApplied("Http", "UseHeadlessTenancy");

        // then
        var seam = manifest.GetSeam("Http");
        seam.Should().NotBeNull();
        seam!.Capabilities.Should().BeEquivalentTo("resolve-from-claims");
        seam.RuntimeMarkers.Should().BeEquivalentTo("UseHeadlessTenancy");
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
