// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>
/// Emits a startup error when messaging tenant propagation was configured but no other Headless
/// tenancy seam (HTTP claim resolution, EntityFramework, Mediator) is recorded in the posture
/// manifest AND no consumer-supplied <see cref="ICurrentTenant"/> override is registered.
/// Propagation under those conditions would be a silent no-op, and silent no-op propagation is hard
/// to diagnose at runtime.
/// </summary>
internal sealed class TenantPropagationStartupValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var messagingSeam = context.Manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        if (
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.PropagateTenantCapability,
                StringComparer.Ordinal
            ) != true
        )
        {
            yield break;
        }

        var otherSeamsContributeTenant = context.Manifest.Seams.Any(seam =>
            !string.Equals(seam.Seam, HeadlessMessagingTenancyBuilder.Seam, StringComparison.Ordinal)
        );

        if (otherSeamsContributeTenant || TenantSourceMissing.HasConsumerOverride(context.Services))
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessMessagingTenancyBuilder.Seam,
            "HEADLESS_TENANCY_MESSAGING_PROPAGATION_NULL_CURRENT_TENANT",
            "Headless messaging tenant propagation was configured but no other Headless tenancy seam "
                + "(HTTP claim resolution, EntityFramework, Mediator) is recorded and no consumer-supplied "
                + "ICurrentTenant override was registered — propagation would be a silent no-op. Register a "
                + "real tenant source via AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims())), "
                + "AddHeadlessDbContextServices(), or a custom ICurrentTenant implementation BEFORE calling "
                + "AddHeadlessMessaging."
        );
    }
}
