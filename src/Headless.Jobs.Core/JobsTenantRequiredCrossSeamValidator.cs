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
