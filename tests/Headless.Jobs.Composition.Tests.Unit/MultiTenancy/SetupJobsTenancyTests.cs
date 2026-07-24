// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.MultiTenancy;

/// <summary>
/// Tests covering <see cref="SetupJobsTenancy"/> Jobs tenancy registration through the root
/// <c>AddHeadlessTenancy</c> surface: posture recording, idempotent DI, and the three Tier-1 startup
/// validators (isolated strict-mode warning, propagation-null-current-tenant error, options-clobber error).
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class SetupJobsTenancyTests : TestBase, IDisposable
{
    // AddHeadlessJobs (used by the real-host propagation test) freezes the process-global discovery registry;
    // re-arm it around each test so nothing leaks into the sibling Jobs tests in this collection.
    public SetupJobsTenancyTests() => JobFunctionProvider.ResetForTests(discoveryComplete: false);

    public void Dispose() => JobFunctionProvider.ResetForTests();

    [Fact]
    public void propagate_tenant_records_propagating_posture_and_enables_the_option()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant()));

        // then
        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<JobsTenancyOptions>>().Value.PropagateTenant.Should().BeTrue();

        var seam = provider.GetRequiredService<TenantPostureManifest>().GetSeam(HeadlessJobsTenancyBuilder.Seam);
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Propagating);
        seam.Capabilities.Should().BeEquivalentTo(HeadlessJobsTenancyBuilder.PropagateTenantCapability);
    }

    [Fact]
    public void require_tenant_on_enqueue_records_enforcing_posture_and_enables_strict()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue()));

        // then
        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<JobsTenancyOptions>>().Value.TenantContextRequired.Should().BeTrue();

        var seam = provider.GetRequiredService<TenantPostureManifest>().GetSeam(HeadlessJobsTenancyBuilder.Seam);
        seam.Should().NotBeNull();
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should()
            .BeEquivalentTo(
                HeadlessJobsTenancyBuilder.PropagateTenantCapability,
                HeadlessJobsTenancyBuilder.RequireTenantOnEnqueueCapability
            );
    }

    [Fact]
    public void calling_the_root_twice_does_not_duplicate_validators_or_sentinels_or_throw()
    {
        // given
        var builder = Host.CreateApplicationBuilder();

        // when — repeated root configuration must not double-register any validator, sentinel, or PostConfigure.
        var act = () =>
        {
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
            );
            builder.AddHeadlessTenancy(tenancy =>
                tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue())
            );
        };
        act.Should().NotThrow();

        // then
        using var provider = builder.Services.BuildServiceProvider();
        var validators = provider.GetServices<IHeadlessTenancyValidator>().ToArray();
        validators.OfType<JobsTenantPropagationStartupValidator>().Should().ContainSingle();
        validators.OfType<JobsTenantRequiredCrossSeamValidator>().Should().ContainSingle();
        validators.OfType<JobsTenantRequiredStartupValidator>().Should().ContainSingle();

        builder.Services.Count(d => d.ServiceType == typeof(PropagateTenantSentinel)).Should().Be(1);
        builder.Services.Count(d => d.ServiceType == typeof(RequireTenantOnEnqueueSentinel)).Should().Be(1);

        // The strengthen-only posture merge keeps a single Enforcing seam despite the repeat.
        var seam = provider.GetRequiredService<TenantPostureManifest>().GetSeam(HeadlessJobsTenancyBuilder.Seam);
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
    }

    [Fact]
    public void emits_isolated_warning_when_strict_without_propagation_and_no_other_tenant_source()
    {
        // given — strict enqueue with no propagation, no other seam, no consumer ICurrentTenant override.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.RequireTenantOnEnqueue()));

        using var provider = builder.Services.BuildServiceProvider();
        var (validator, context) = _Resolve<JobsTenantRequiredCrossSeamValidator>(provider);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle();
        diagnostics[0].Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Warning);
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_ISOLATED");
        diagnostics[0].Seam.Should().Be(HeadlessJobsTenancyBuilder.Seam);
    }

    [Fact]
    public void emits_no_isolated_warning_when_propagation_is_enabled()
    {
        // given — strict-with-propagation is a supported posture: schedule-side capture populates the tenant.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant().RequireTenantOnEnqueue()));

        using var provider = builder.Services.BuildServiceProvider();
        var (validator, context) = _Resolve<JobsTenantRequiredCrossSeamValidator>(provider);

        // when / then
        validator.Validate(context).Should().BeEmpty();
    }

    [Fact]
    public void emits_propagation_error_when_only_the_jobs_accessor_fallback_current_tenant_is_registered()
    {
        // given — a real Jobs host (KTD1) registers the accessor-backed CurrentTenant fallback whose Id stays
        // null. With propagation on but no other seam or consumer override, propagation would be a silent no-op.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLogging();
        builder.Services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant()));

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
        var (validator, context) = _Resolve<JobsTenantPropagationStartupValidator>(provider);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle();
        diagnostics[0].Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_JOBS_PROPAGATION_NULL_CURRENT_TENANT");
        diagnostics[0].Seam.Should().Be(HeadlessJobsTenancyBuilder.Seam);
    }

    [Fact]
    public void emits_propagation_error_when_only_consumer_seams_are_recorded()
    {
        // given — a Messaging-style consumer seam records posture but does not populate ICurrentTenant, so it
        // must not count as a tenant source (fail-open regression guard).
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.RecordSeam("Messaging", TenantPostureStatus.Propagating, "propagate-tenant");
            tenancy.Jobs(jobs => jobs.PropagateTenant());
        });

        using var provider = builder.Services.BuildServiceProvider();
        var (validator, context) = _Resolve<JobsTenantPropagationStartupValidator>(provider);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle();
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_JOBS_PROPAGATION_NULL_CURRENT_TENANT");
    }

    [Fact]
    public void emits_no_propagation_error_when_the_http_claims_seam_is_recorded()
    {
        // given — the HTTP claim-resolution seam is the seam that actually populates ICurrentTenant.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
        {
            tenancy.RecordSeam("Http", TenantPostureStatus.Configured, "resolve-from-claims");
            tenancy.Jobs(jobs => jobs.PropagateTenant());
        });

        using var provider = builder.Services.BuildServiceProvider();
        var (validator, context) = _Resolve<JobsTenantPropagationStartupValidator>(provider);

        // when / then
        validator.Validate(context).Should().BeEmpty();
    }

    [Fact]
    public void emits_no_propagation_error_when_a_consumer_supplied_current_tenant_is_registered()
    {
        // given — a consumer-supplied (non-CurrentTenant, non-NullCurrentTenant) ICurrentTenant is a real source.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(Substitute.For<ICurrentTenant>());
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.PropagateTenant()));

        using var provider = builder.Services.BuildServiceProvider();
        var (validator, context) = _Resolve<JobsTenantPropagationStartupValidator>(provider);

        // when / then
        validator.Validate(context).Should().BeEmpty();
    }

    [Fact]
    public void emits_clobber_error_when_a_later_post_configure_disables_strict()
    {
        // given — a later PostConfigure runs after the seam's PostConfigure and clobbers TenantContextRequired.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.RequireTenantOnEnqueue()));
        builder.Services.PostConfigure<JobsTenancyOptions>(options => options.TenantContextRequired = false);

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<JobsTenancyOptions>>().Value.TenantContextRequired.Should().BeFalse();
        var (validator, context) = _Resolve<JobsTenantRequiredStartupValidator>(provider);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle();
        diagnostics[0].Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_DISABLED");
        diagnostics[0].Seam.Should().Be(HeadlessJobsTenancyBuilder.Seam);
    }

    [Fact]
    public void reject_cross_tenant_enqueue_records_enforcing_posture_and_enables_the_option()
    {
        // given — repeated calls exercise the sentinel idempotency too.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy =>
            tenancy.Jobs(jobs => jobs.RejectCrossTenantEnqueue().RejectCrossTenantEnqueue())
        );

        using var provider = builder.Services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<JobsTenancyOptions>>().Value.RejectCrossTenantEnqueue.Should().BeTrue();
        builder.Services.Count(d => d.ServiceType == typeof(RejectCrossTenantEnqueueSentinel)).Should().Be(1);
        var seam = provider.GetRequiredService<TenantPostureManifest>().GetSeam(HeadlessJobsTenancyBuilder.Seam);
        seam!.Status.Should().Be(TenantPostureStatus.Enforcing);
        seam.Capabilities.Should().Contain(HeadlessJobsTenancyBuilder.RejectCrossTenantEnqueueCapability);
    }

    [Fact]
    public void emits_clobber_error_when_a_later_post_configure_disables_cross_tenant_rejection()
    {
        // given — a later PostConfigure clobbers the seam's contribution.
        var builder = Host.CreateApplicationBuilder();
        builder.AddHeadlessTenancy(tenancy => tenancy.Jobs(jobs => jobs.RejectCrossTenantEnqueue()));
        builder.Services.PostConfigure<JobsTenancyOptions>(options => options.RejectCrossTenantEnqueue = false);

        using var provider = builder.Services.BuildServiceProvider();
        var (validator, context) = _Resolve<JobsCrossTenantRejectionStartupValidator>(provider);

        // when
        var diagnostics = validator.Validate(context).ToArray();

        // then
        diagnostics.Should().ContainSingle();
        diagnostics[0].Severity.Should().Be(HeadlessTenancyDiagnosticSeverity.Error);
        diagnostics[0].Code.Should().Be("HEADLESS_TENANCY_JOBS_REJECT_CROSS_TENANT_DISABLED");
        diagnostics[0].Seam.Should().Be(HeadlessJobsTenancyBuilder.Seam);
    }

    [Fact]
    public void the_validators_inject_no_io_dependencies()
    {
        // Tier-1 guarantee: the validators depend only on in-memory options, never an I/O client. Constructor
        // parameter types must stay within the allowed set (or be parameterless).
        Type[] validatorTypes =
        [
            typeof(JobsTenantRequiredCrossSeamValidator),
            typeof(JobsTenantPropagationStartupValidator),
            typeof(JobsTenantRequiredStartupValidator),
            typeof(JobsCrossTenantRejectionStartupValidator),
        ];

        foreach (var type in validatorTypes)
        {
            var constructor = type.GetConstructors().Should().ContainSingle().Subject;
            constructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Should()
                .BeSubsetOf([typeof(IOptions<JobsTenancyOptions>)]);
        }
    }

    private static (T Validator, HeadlessTenancyValidationContext Context) _Resolve<T>(IServiceProvider provider)
        where T : IHeadlessTenancyValidator
    {
        var validator = provider.GetServices<IHeadlessTenancyValidator>().OfType<T>().Single();
        var manifest = provider.GetRequiredService<TenantPostureManifest>();

        return (validator, new HeadlessTenancyValidationContext(provider, manifest));
    }
}
