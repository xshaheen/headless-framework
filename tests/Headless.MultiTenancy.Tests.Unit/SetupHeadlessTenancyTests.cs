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
        builder
            .Services.Where(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest))
            .Should()
            .ContainSingle();
        builder
            .Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && string.Equals(
                    descriptor.ImplementationType?.Name,
                    "HeadlessTenancyStartupValidator",
                    StringComparison.Ordinal
                )
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

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );

        // when — validation runs in StartingAsync so it fires before any other hosted service's StartAsync.
        var act = () => hostedService.StartingAsync(CancellationToken.None);

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

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );

        // when
        var act = () => hostedService.StartingAsync(CancellationToken.None);

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
        allStrings
            .Should()
            .NotContain(sentinelTenantA)
            .And.NotContain(sentinelTenantB)
            .And.NotContain(sentinelClaimValue);
        allStrings
            .Should()
            .AllSatisfy(value =>
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

    [Fact]
    public async Task should_emit_synthetic_validator_threw_diagnostic_and_continue_when_a_validator_throws()
    {
        // given — a throwing validator registered ahead of a well-behaved error-returning one
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(new ThrowingValidator("validator-blew-up"));
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(
            new TestValidator("HEADLESS_OTHER", "Other seam failed.")
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.RecordSeam("Http", TenantPostureStatus.Configured));

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );

        // when
        var act = () => hostedService.StartingAsync(CancellationToken.None);

        // then — the throw is converted to a synthetic diagnostic AND iteration continues to the next validator
        var exception = (await act.Should().ThrowAsync<HeadlessTenancyValidationException>()).Which;
        exception.Message.Should().Contain("VALIDATOR_THREW").And.Contain("HEADLESS_OTHER");
        exception
            .Diagnostics.Should()
            .Contain(diagnostic => diagnostic.Code == "VALIDATOR_THREW")
            .And.Contain(diagnostic => diagnostic.Code == "HEADLESS_OTHER");
    }

    [Fact]
    public async Task should_attach_failing_diagnostics_to_the_thrown_exception()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(new TestValidator());
        builder.AddHeadlessTenancy(tenancy => tenancy.RecordSeam("Http", TenantPostureStatus.Configured));

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );

        // when
        var act = () => hostedService.StartingAsync(CancellationToken.None);

        // then — the structured diagnostics are recoverable from the typed exception, not just the message
        var exception = (await act.Should().ThrowAsync<HeadlessTenancyValidationException>()).Which;
        exception.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("HEADLESS_TEST");
    }

    [Fact]
    public async Task should_not_run_validators_when_startup_cancellation_is_already_requested()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var validator = new CountingValidator();
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(validator);
        builder.AddHeadlessTenancy(_ => { });

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () => hostedService.StartingAsync(cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        validator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task should_propagate_validator_cancellation_without_synthetic_diagnostic()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        var cancellation = new CancellationToken(canceled: true);
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(new CancelingValidator(cancellation));
        builder.Services.AddSingleton<IHeadlessTenancyValidator>(
            new TestValidator("HEADLESS_OTHER", "Other seam failed.")
        );
        builder.AddHeadlessTenancy(_ => { });

        await using var provider = builder.Services.BuildServiceProvider();
        var hostedService = (IHostedLifecycleService)
            provider
                .GetServices<IHostedService>()
                .Single(service =>
                    string.Equals(service.GetType().Name, "HeadlessTenancyStartupValidator", StringComparison.Ordinal)
                );

        // when
        var act = () => hostedService.StartingAsync(CancellationToken.None);

        // then
        var exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.Should().Be(cancellation);
    }

    [Fact]
    public void should_replace_factory_manifest_registration_with_a_singleton_instance()
    {
        // given — a consumer pre-registers the manifest via a factory (the documented blind-spot)
        var services = new ServiceCollection();
        services.AddSingleton<TenantPostureManifest>(_ => new TenantPostureManifest());

        // when
        var manifest = services.GetOrAddTenantPostureManifest();

        // then — the factory descriptor is replaced by a single instance descriptor holding the returned manifest
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest))
            .Should()
            .ContainSingle()
            .Which.ImplementationInstance.Should()
            .BeSameAs(manifest);
    }

    [Fact]
    public void should_return_pre_registered_manifest_instance_without_adding_a_second_descriptor()
    {
        // given — a consumer pre-registers a manifest instance before AddHeadlessTenancy runs
        var services = new ServiceCollection();
        var custom = new TenantPostureManifest();
        services.AddSingleton(custom);

        // when
        var manifest = services.GetOrAddTenantPostureManifest();

        // then — the consumer's instance is reused, not shadowed by a second registration
        manifest.Should().BeSameAs(custom);
        services.Where(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest)).Should().ContainSingle();
    }

    [Fact]
    public void should_discard_pre_registered_instance_when_a_later_factory_registration_exists()
    {
        // given — pins the documented footgun: reconciliation inspects the LAST registration, so an
        // instance followed by a factory loses both the instance and any posture recorded on it
        var services = new ServiceCollection();
        var custom = new TenantPostureManifest();
        custom.RecordSeam("Http", TenantPostureStatus.Enforcing);
        services.AddSingleton(custom);
        services.AddSingleton<TenantPostureManifest>(_ => new TenantPostureManifest());

        // when
        var manifest = services.GetOrAddTenantPostureManifest();

        // then — a fresh manifest replaces all prior registrations; the recorded posture is gone
        manifest.Should().NotBeSameAs(custom);
        manifest.GetSeam("Http").Should().BeNull();
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TenantPostureManifest))
            .Should()
            .ContainSingle()
            .Which.ImplementationInstance.Should()
            .BeSameAs(manifest);
    }

    [Theory]
    [InlineData(TenantPostureStatus.Guarded, TenantPostureStatus.Enforcing, TenantPostureStatus.Enforcing)]
    [InlineData(TenantPostureStatus.Enforcing, TenantPostureStatus.Guarded, TenantPostureStatus.Enforcing)]
    [InlineData(TenantPostureStatus.Propagating, TenantPostureStatus.Guarded, TenantPostureStatus.Guarded)]
    [InlineData(TenantPostureStatus.Configured, TenantPostureStatus.Propagating, TenantPostureStatus.Propagating)]
    public void should_keep_the_strongest_status_regardless_of_record_order(
        TenantPostureStatus first,
        TenantPostureStatus second,
        TenantPostureStatus expected
    )
    {
        // given
        var manifest = new TenantPostureManifest();

        // when
        manifest.RecordSeam("Seam", first);
        manifest.RecordSeam("Seam", second);

        // then
        manifest.GetSeam("Seam")!.Status.Should().Be(expected);
    }

    [Fact]
    public void should_throw_when_merging_an_undefined_status_value()
    {
        // given
        var manifest = new TenantPostureManifest();
        manifest.RecordSeam("Seam", TenantPostureStatus.Enforcing);

        // when — an out-of-range cast must fail loudly instead of silently down-ranking the seam
        var act = () => manifest.RecordSeam("Seam", (TenantPostureStatus)99);

        // then — Argument.IsInEnum surfaces an undefined enum value as InvalidEnumArgumentException
        act.Should().Throw<System.ComponentModel.InvalidEnumArgumentException>();
    }

    [Theory]
    [InlineData(null, "code", "message")]
    [InlineData(" ", "code", "message")]
    [InlineData("seam", "", "message")]
    [InlineData("seam", "code", "  ")]
    public void should_reject_blank_diagnostic_fields(string? seam, string code, string message)
    {
        // when — direct construction must validate, not only the static factories
        var act = () => new HeadlessTenancyDiagnostic(seam!, code, message, HeadlessTenancyDiagnosticSeverity.Error);

        // then
        act.Should().Throw<ArgumentException>();
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

    private sealed class ThrowingValidator(string message) : IHeadlessTenancyValidator
    {
        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CancelingValidator(CancellationToken cancellationToken) : IHeadlessTenancyValidator
    {
        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class CountingValidator : IHeadlessTenancyValidator
    {
        public int Calls { get; private set; }

        public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
        {
            Calls++;

            return [];
        }
    }
}
