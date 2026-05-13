// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.Mediator;

[PublicAPI]
public static class SetupMediatorTenancy
{
    /// <summary>Configures Mediator tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The Mediator tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Mediator(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessMediatorTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessMediatorTenancyBuilder(builder));

        return builder;
    }
}

/// <summary>Records tenant posture for Mediator request handling.</summary>
[PublicAPI]
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
        _builder.RecordSeam("Mediator", TenantPostureStatus.Enforcing, "require-tenant");

        return this;
    }
}
