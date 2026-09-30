// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>Configures tenant telemetry enrichment through the root Headless tenancy builder.</summary>
[PublicAPI]
public static class SetupHeadlessTenancyTelemetry
{
    /// <summary>
    /// Configures <see cref="TenantTelemetryOptions"/>: the log scope property and span/metric attribute names, and
    /// which channels are enriched. Enrichment is on with the defaults when this is never called.
    /// </summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The options callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyBuilder Telemetry(
        this HeadlessTenancyBuilder builder,
        Action<TenantTelemetryOptions> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        builder.Services.Configure<TenantTelemetryOptions, TenantTelemetryOptionsValidator>(configure);

        return builder;
    }
}
