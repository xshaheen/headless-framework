// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

[PublicAPI]
public static class SetupBlobsTenancy
{
    /// <summary>Configures blob storage tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The blob tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Blobs(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessBlobsTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessBlobsTenancyBuilder(builder));

        return builder;
    }
}
