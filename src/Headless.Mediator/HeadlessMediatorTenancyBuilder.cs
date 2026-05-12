// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.Mediator;

/// <summary>Records tenant posture for Mediator request handling.</summary>
public sealed class HeadlessMediatorTenancyBuilder
{
    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessMediatorTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Adds the tenant-required Mediator pipeline behavior.</summary>
    /// <returns>The same Mediator tenancy builder.</returns>
    public HeadlessMediatorTenancyBuilder RequireTenant()
    {
        _builder.Services.AddTenantRequiredBehavior();
        _builder.RecordSeam("Mediator", TenantPostureStatuses.Enforcing, "require-tenant");

        return this;
    }
}
