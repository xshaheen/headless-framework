// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Jobs;

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

    /// <summary>Capability label reported by <see cref="RejectCrossTenantEnqueue"/>.</summary>
    public const string RejectCrossTenantEnqueueCapability = "reject-cross-tenant-enqueue";

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
        // HeadlessTenancyStartupValidator (an IHeadlessStartupValidator) — runs before any
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

    /// <summary>
    /// Rejects an enqueue whose explicit tenant differs from the present ambient tenant (the lateral
    /// tenant-to-tenant path). Without this, explicit values win and a mismatch logs a warning — an in-process
    /// guardrail against accidental cross-tenant scheduling, not a security boundary. Explicit values supplied
    /// from system scope (no ambient tenant) are always honored, so cron fan-out is unaffected.
    /// </summary>
    /// <returns>The same Jobs tenancy builder.</returns>
    public HeadlessJobsTenancyBuilder RejectCrossTenantEnqueue()
    {
        _RegisterSentinelOnce<RejectCrossTenantEnqueueSentinel>(options => options.RejectCrossTenantEnqueue = true);

        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, JobsCrossTenantRejectionStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Enforcing, RejectCrossTenantEnqueueCapability);

        return this;
    }

    // Sentinel — the PostConfigure contribution must register at most once per flag, so repeated builder calls
    // (or a repeated .Jobs(...) registration) do not stack duplicate callbacks.
    private void _RegisterSentinelOnce<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSentinel
    >(Action<JobsTenancyOptions> postConfigure)
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
