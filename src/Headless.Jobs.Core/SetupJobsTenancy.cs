// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Models;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Jobs;

[PublicAPI]
public static class SetupJobsTenancy
{
    /// <summary>Configures Jobs tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The Jobs tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder Jobs(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessJobsTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessJobsTenancyBuilder(builder));

        return builder;
    }
}
