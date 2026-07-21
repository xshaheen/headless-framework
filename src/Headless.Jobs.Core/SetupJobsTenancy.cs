// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Jobs;

[PublicAPI]
public static class SetupJobsTenancy
{
    /// <summary>Configures Jobs tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The Jobs tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Jobs(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessJobsTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessJobsTenancyBuilder(builder));

        return builder;
    }
}

/// <summary>Records tenant posture for Headless jobs.</summary>
[PublicAPI]
public sealed class HeadlessJobsTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Jobs";

    /// <summary>Capability label reported by <see cref="PropagateTenant"/>.</summary>
    public const string PropagateTenantCapability = "propagate-tenant";

    /// <summary>Capability label reported by <see cref="RequireTenantOnEnqueue"/>.</summary>
    public const string RequireTenantOnEnqueueCapability = "require-tenant-on-enqueue";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessJobsTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Captures the ambient tenant onto time jobs at schedule time when no explicit value is supplied.</summary>
    /// <returns>The same Jobs tenancy builder.</returns>
    public HeadlessJobsTenancyBuilder PropagateTenant()
    {
        // AddHeadlessJobs already registers the tenant-context primitives (accessor + ICurrentTenant fallback)
        // and the always-on, options-gated schedule/execute middleware (see
        // Headless.Jobs.Core/DependencyInjection/SetupJobs.cs _AddTenancyServices), so — unlike the Messaging
        // seam that adds propagation middleware — the Jobs seam only flips the option flag the middleware reads.
        _RegisterSentinelOnce<PropagateTenantSentinel>(options => options.PropagateTenant = true);

        // Routes through the unified IHeadlessTenancyValidator collection aggregated by
        // HeadlessTenancyStartupValidator (IHostedLifecycleService) — runs in StartingAsync before any
        // IHostedService.StartAsync so a misconfigured tenancy posture fails fast.
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, JobsTenantPropagationStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Propagating, PropagateTenantCapability);

        return this;
    }

    /// <summary>Requires a time-job enqueue to resolve an explicit or ambient tenant unless the job is a system job.</summary>
    /// <returns>The same Jobs tenancy builder.</returns>
    public HeadlessJobsTenancyBuilder RequireTenantOnEnqueue()
    {
        _RegisterSentinelOnce<RequireTenantOnEnqueueSentinel>(options => options.TenantContextRequired = true);

        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, JobsTenantRequiredCrossSeamValidator>()
        );
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, JobsTenantRequiredStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Enforcing, RequireTenantOnEnqueueCapability);

        return this;
    }

    // Sentinel — the PostConfigure contribution must register at most once per flag, so repeated builder calls
    // (or a repeated .Jobs(...) registration) do not stack duplicate callbacks.
    private void _RegisterSentinelOnce<TSentinel>(Action<JobsTenancyOptions> postConfigure)
        where TSentinel : class
    {
        if (_builder.Services.Any(descriptor => descriptor.ServiceType == typeof(TSentinel)))
        {
            return;
        }

        _builder.Services.AddSingleton<TSentinel>();
        _builder.Services.PostConfigure(postConfigure);
    }
}

/// <summary>Sentinel for one-shot PropagateTenant PostConfigure registration.</summary>
internal sealed class PropagateTenantSentinel;

/// <summary>Sentinel for one-shot RequireTenantOnEnqueue PostConfigure registration.</summary>
internal sealed class RequireTenantOnEnqueueSentinel;

/// <summary>
/// Emits a startup warning when <c>RequireTenantOnEnqueue()</c> is configured in isolation: strict enforcement
/// is on, the Jobs seam does not also propagate the ambient tenant, no other Headless tenancy seam (HTTP claim
/// resolution, EntityFramework, Mediator) contributes it, and no consumer-supplied <see cref="ICurrentTenant"/>
/// override is registered. Under those conditions every non-system enqueue that omits an explicit tenant fails.
/// The diagnostic is a warning rather than an error so hosts that always pass an explicit <c>TenantId</c> are not
/// blocked; the cross-seam validator only fires when no other tenancy seam recorded posture. Propagation on the
/// same seam captures the ambient at enqueue time, so strict-with-propagation is a supported posture and is
/// excluded here.
/// </summary>
internal sealed class JobsTenantRequiredCrossSeamValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var jobsSeam = context.Manifest.GetSeam(HeadlessJobsTenancyBuilder.Seam);

        if (
            jobsSeam?.Capabilities.Contains(
                HeadlessJobsTenancyBuilder.RequireTenantOnEnqueueCapability,
                StringComparer.Ordinal
            ) != true
        )
        {
            yield break;
        }

        // Strict-with-propagation is supported: schedule-side capture populates the tenant at enqueue time.
        if (
            jobsSeam.Capabilities.Contains(HeadlessJobsTenancyBuilder.PropagateTenantCapability, StringComparer.Ordinal)
        )
        {
            yield break;
        }

        var otherSeamsContributeTenant = context.Manifest.Seams.Any(seam =>
            !string.Equals(seam.Seam, HeadlessJobsTenancyBuilder.Seam, StringComparison.Ordinal)
        );

        if (otherSeamsContributeTenant || TenantSourceMissing.HasConsumerOverride(context.Services))
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Warning(
            HeadlessJobsTenancyBuilder.Seam,
            "HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_ISOLATED",
            "RequireTenantOnEnqueue() is configured but Jobs tenant propagation is off and no other Headless "
                + "tenancy seam (HTTP claim resolution, EntityFramework, Mediator) or consumer-supplied "
                + "ICurrentTenant registration contributes the ambient tenant a non-system enqueue requires. "
                + "Enable PropagateTenant(), add a tenant source, or always pass an explicit TenantId."
        );
    }
}

/// <summary>Shared "no tenant source" predicate used by the Jobs tenancy validators.</summary>
internal static class TenantSourceMissing
{
    /// <summary>
    /// Returns <see langword="true"/> when the host registered a custom <see cref="ICurrentTenant"/> implementation
    /// beyond the framework defaults (<c>CurrentTenant</c> or <c>NullCurrentTenant</c>). Used by tenancy validators
    /// to recognize consumer-supplied tenant sources that bypass the seam manifest.
    /// </summary>
    public static bool HasConsumerOverride(IServiceProvider services)
    {
        var currentTenant = services.GetService<ICurrentTenant>();
        return currentTenant is not null and not CurrentTenant and not NullCurrentTenant;
    }
}

/// <summary>
/// Emits a startup error when Jobs tenant propagation was configured but no other Headless tenancy seam
/// (HTTP claim resolution, EntityFramework, Mediator) is recorded AND no consumer-supplied
/// <see cref="ICurrentTenant"/> override is registered. AddHeadlessJobs always registers the accessor-backed
/// <c>CurrentTenant</c> fallback whose <c>Id</c> stays null until a seam populates the AsyncLocal, so under those
/// conditions the resolved tenant is effectively null and propagation would be a silent no-op — which is hard to
/// diagnose at runtime.
/// </summary>
internal sealed class JobsTenantPropagationStartupValidator : IHeadlessTenancyValidator
{
    // Mirrors Headless.Api.Core's SetupApiTenancy seam/capability literals (Jobs.Core has no Api reference).
    // Only the HTTP claim-resolution seam produces ambient tenant context today; consumer seams (Messaging,
    // EntityFramework guards, Authorization) record posture without populating ICurrentTenant, so counting
    // them as tenant sources would fail open and hide a silent-no-op propagation setup.
    private const string _HttpSeam = "Http";
    private const string _ResolveFromClaimsCapability = "resolve-from-claims";

    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var jobsSeam = context.Manifest.GetSeam(HeadlessJobsTenancyBuilder.Seam);

        if (
            jobsSeam?.Capabilities.Contains(
                HeadlessJobsTenancyBuilder.PropagateTenantCapability,
                StringComparer.Ordinal
            ) != true
        )
        {
            yield break;
        }

        var otherSeamsContributeTenant = context.Manifest.Seams.Any(seam =>
            string.Equals(seam.Seam, _HttpSeam, StringComparison.Ordinal)
            && seam.Capabilities.Contains(_ResolveFromClaimsCapability, StringComparer.Ordinal)
        );

        if (otherSeamsContributeTenant || TenantSourceMissing.HasConsumerOverride(context.Services))
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessJobsTenancyBuilder.Seam,
            "HEADLESS_TENANCY_JOBS_PROPAGATION_NULL_CURRENT_TENANT",
            "Headless jobs tenant propagation was configured but no other Headless tenancy seam (HTTP claim "
                + "resolution, EntityFramework, Mediator) is recorded and no consumer-supplied ICurrentTenant "
                + "override was registered — the resolved ICurrentTenant is only the accessor fallback whose Id "
                + "stays null, so propagation would be a silent no-op. Register a real tenant source via "
                + "AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims())), "
                + "AddHeadlessDbContextServices(), or a custom ICurrentTenant implementation BEFORE calling "
                + "AddHeadlessJobs."
        );
    }
}

/// <summary>
/// Emits a startup error when the Jobs seam recorded <c>require-tenant-on-enqueue</c> posture but
/// <see cref="JobsTenancyOptions.TenantContextRequired"/> resolves to <see langword="false"/> (typically because
/// a later <c>PostConfigure&lt;JobsTenancyOptions&gt;</c> or <c>Configure&lt;JobsTenancyOptions&gt;</c> call
/// clobbered the <c>PostConfigure</c> contribution applied by <c>RequireTenantOnEnqueue()</c>). Surfaces the
/// mismatch at startup so operators are not surprised by silent loss of the strict-enqueue guard.
/// </summary>
internal sealed class JobsTenantRequiredStartupValidator(IOptions<JobsTenancyOptions> options)
    : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var jobsSeam = context.Manifest.GetSeam(HeadlessJobsTenancyBuilder.Seam);

        var recordedRequireTenant =
            jobsSeam?.Capabilities.Contains(
                HeadlessJobsTenancyBuilder.RequireTenantOnEnqueueCapability,
                StringComparer.Ordinal
            ) == true;

        if (!recordedRequireTenant || options.Value.TenantContextRequired)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessJobsTenancyBuilder.Seam,
            "HEADLESS_TENANCY_JOBS_REQUIRE_TENANT_DISABLED",
            "Headless jobs seam recorded require-tenant-on-enqueue but JobsTenancyOptions.TenantContextRequired "
                + "resolved to false at startup. A later PostConfigure/Configure<JobsTenancyOptions>(...) call "
                + "clobbered the PostConfigure contribution applied by RequireTenantOnEnqueue(). Move the override "
                + "before AddHeadlessTenancy(...) or remove it."
        );
    }
}
