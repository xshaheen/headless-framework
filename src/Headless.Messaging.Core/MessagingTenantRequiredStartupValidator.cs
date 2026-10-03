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
/// Emits a startup error when the messaging seam recorded <c>require-tenant-on-publish</c> posture
/// but <see cref="MessagingOptions.TenantContextRequired"/> resolves to <see langword="false"/>
/// (typically because a later <c>Configure&lt;MessagingOptions&gt;</c> call clobbered the
/// <c>PostConfigure</c> contribution). Surfaces the mismatch at startup so operators are not
/// surprised by silent loss of the strict-publish guard.
/// </summary>
internal sealed class MessagingTenantRequiredStartupValidator(IOptions<MessagingOptions> options)
    : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        var messagingSeam = context.Manifest.GetSeam(HeadlessMessagingTenancyBuilder.Seam);

        var recordedRequireTenant =
            messagingSeam?.Capabilities.Contains(
                HeadlessMessagingTenancyBuilder.RequireTenantOnPublishCapability,
                StringComparer.Ordinal
            ) == true;

        if (!recordedRequireTenant || options.Value.TenantContextRequired)
        {
            yield break;
        }

        yield return HeadlessTenancyDiagnostic.Error(
            HeadlessMessagingTenancyBuilder.Seam,
            "HEADLESS_TENANCY_MESSAGING_REQUIRE_TENANT_DISABLED",
            "Headless messaging seam recorded require-tenant-on-publish but MessagingOptions.TenantContextRequired "
                + "resolved to false at startup. A later Configure<MessagingOptions>(...) call clobbered the "
                + "PostConfigure contribution applied by RequireTenantOnPublish(). Move the override before "
                + "AddHeadlessTenancy(...) or remove it."
        );
    }
}
