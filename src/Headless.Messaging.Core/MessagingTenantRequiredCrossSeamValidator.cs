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
/// Emits a startup warning when <c>RequireTenantOnPublish()</c> is configured in isolation: no other
/// seam (HTTP claim resolution, tenant propagation, or a real <see cref="ICurrentTenant"/>) contributes
/// the ambient tenant that publishes will require. The diagnostic is a warning rather than an error so
/// hosts that deliberately resolve the tenant via custom <c>ICurrentTenant</c> registrations are not
/// blocked at startup; the cross-seam validator only fires when no other tenancy seam recorded posture.
/// </summary>
internal sealed class MessagingTenantRequiredCrossSeamValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var messagingSeam = context.Manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        if (
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.RequireTenantOnPublishCapability,
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

        yield return HeadlessTenancyDiagnostic.Warning(
            HeadlessMessagingTenancyBuilder.Seam,
            "HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_ISOLATED",
            "RequireTenantOnPublish() is configured but no other Headless tenancy seam (HTTP claim "
                + "resolution, tenant propagation, EntityFramework, or a real ICurrentTenant registration) "
                + "contributes the ambient tenant required at publish time. Add a tenant source or register "
                + "a real ICurrentTenant before AddHeadlessMessaging."
        );
    }
}
