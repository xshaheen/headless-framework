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

/// <summary>Records tenant posture for Headless messaging.</summary>
[PublicAPI]
public sealed class HeadlessMessagingTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Messaging";

    /// <summary>Capability label reported by <see cref="PropagateTenant"/>.</summary>
    public const string PropagateTenantCapability = "propagate-tenant";

    /// <summary>Capability label reported by <see cref="RequireTenantOnPublish"/>.</summary>
    public const string RequireTenantOnPublishCapability = "require-tenant-on-publish";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessMessagingTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Registers publish and consume middleware that propagates tenant context through messages.</summary>
    /// <returns>The same messaging tenancy builder.</returns>
    public HeadlessMessagingTenancyBuilder PropagateTenant()
    {
        new MessagingBuilder(_builder.Services).AddTenantPropagationServices();
        _builder.RecordSeam(Seam, TenantPostureStatus.Propagating, PropagateTenantCapability);

        return this;
    }

    /// <summary>Requires publish calls to resolve a tenant from publish options or ambient tenant context.</summary>
    /// <returns>The same messaging tenancy builder.</returns>
    public HeadlessMessagingTenancyBuilder RequireTenantOnPublish()
    {
        // Sentinel — guard the PostConfigure registration so repeated RequireTenantOnPublish()
        // calls do not register the same callback twice. The sentinel marker registration uses
        // a singleton presence check to ensure exactly-once PostConfigure wiring.
        if (_builder.Services.All(d => d.ServiceType != typeof(RequireTenantOnPublishSentinel)))
        {
            _builder.Services.AddSingleton<RequireTenantOnPublishSentinel>();
            _builder.Services.PostConfigure<MessagingOptions>(options => options.TenantContextRequired = true);
        }

        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, MessagingTenantRequiredCrossSeamValidator>()
        );
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, MessagingTenantRequiredStartupValidator>()
        );
        _builder.RecordSeam(Seam, TenantPostureStatus.Enforcing, RequireTenantOnPublishCapability);

        return this;
    }
}
