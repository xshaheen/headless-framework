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
