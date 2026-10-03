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
