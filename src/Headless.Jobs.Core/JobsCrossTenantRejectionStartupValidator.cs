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
/// Emits a startup error when the jobs seam recorded <c>reject-cross-tenant-enqueue</c> posture but
/// <see cref="JobsTenancyOptions.RejectCrossTenantEnqueue"/> resolves to <see langword="false"/> (a later
/// <c>Configure&lt;JobsTenancyOptions&gt;</c> call clobbered the <c>PostConfigure</c> contribution). Surfaces the
/// mismatch at startup so operators are not surprised by the silent loss of the lateral guard.
/// </summary>
internal sealed class JobsCrossTenantRejectionStartupValidator(IOptions<JobsTenancyOptions> options)
    : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var jobsSeam = context.Manifest.GetSeam(HeadlessJobsTenancyBuilder.Seam);

        var recordedRejectCrossTenant =
            jobsSeam?.Capabilities.Contains(
                HeadlessJobsTenancyBuilder.RejectCrossTenantEnqueueCapability,
                StringComparer.Ordinal
            ) == true;

        if (!recordedRejectCrossTenant || options.Value.RejectCrossTenantEnqueue)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessJobsTenancyBuilder.Seam,
            "HEADLESS_TENANCY_JOBS_REJECT_CROSS_TENANT_DISABLED",
            "Headless jobs seam recorded reject-cross-tenant-enqueue but "
                + "JobsTenancyOptions.RejectCrossTenantEnqueue resolved to false at startup. A later "
                + "PostConfigure/Configure<JobsTenancyOptions>(...) call clobbered the PostConfigure contribution "
                + "applied by RejectCrossTenantEnqueue(). Move the override before AddHeadlessTenancy(...) or "
                + "remove it."
        );
    }
}
