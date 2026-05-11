// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.MultiTenancy;

/// <summary>Provides the root setup surface for Headless tenant posture configuration.</summary>
[PublicAPI]
public static class HeadlessTenancySetup
{
    /// <summary>
    /// Adds the shared tenancy manifest and lets installed Headless packages contribute seam-specific tenant posture.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">The tenancy configuration callback.</param>
    /// <returns>The same host application builder.</returns>
    public static IHostApplicationBuilder AddHeadlessTenancy(
        this IHostApplicationBuilder builder,
        Action<HeadlessTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        var manifest = builder.Services.AddHeadlessTenancyCore();
        configure(new HeadlessTenancyBuilder(builder, manifest));

        return builder;
    }
}
