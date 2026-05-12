// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.MultiTenancy;

/// <summary>Records tenant posture for Headless messaging.</summary>
public sealed class HeadlessMessagingTenancyBuilder
{
    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessMessagingTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Registers publish and consume filters that propagate tenant context through messages.</summary>
    /// <returns>The same messaging tenancy builder.</returns>
    public HeadlessMessagingTenancyBuilder PropagateTenant()
    {
        _builder.Services.AddTenantPropagationServices();
        _builder.RecordSeam("Messaging", TenantPostureStatuses.Propagating, "propagate-tenant");

        return this;
    }

    /// <summary>Requires publish calls to resolve a tenant from publish options or ambient tenant context.</summary>
    /// <returns>The same messaging tenancy builder.</returns>
    public HeadlessMessagingTenancyBuilder RequireTenantOnPublish()
    {
        _builder.Services.PostConfigure<MessagingOptions>(options => options.TenantContextRequired = true);
        _builder.RecordSeam("Messaging", TenantPostureStatuses.Enforcing, "require-tenant-on-publish");

        return this;
    }
}
