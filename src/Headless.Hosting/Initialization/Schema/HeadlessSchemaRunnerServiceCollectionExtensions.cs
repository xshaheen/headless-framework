// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers schema contributions and the one hosted runner that verifies them at startup.</summary>
[PublicAPI]
public static class HeadlessSchemaRunnerServiceCollectionExtensions
{
    /// <summary>
    /// Registers a feature's schema contribution, built from the service provider when the runner is first resolved
    /// so it can read bound options, and adds the hosted runner once.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="contributionFactory">Builds the contribution.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="contributionFactory"/> is null.</exception>
    public static IServiceCollection AddHeadlessSchemaContribution(
        this IServiceCollection services,
        Func<IServiceProvider, SchemaContribution> contributionFactory
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(contributionFactory);

        // Add, not TryAddEnumerable: every factory descriptor has the same implementation type, so TryAddEnumerable
        // would keep only the first feature's contribution.
        services.AddSingleton(contributionFactory);

        return services.AddHeadlessSchemaRunner();
    }

    /// <summary>
    /// Adds the schema runner, the hosted initializer that verifies the schema at startup, and one stateful resource
    /// per contribution, which <c>resources setup</c> or <c>AddHeadlessResourceSetupOnStartup</c> applies. Idempotent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optionally configures the runner's timeouts.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddHeadlessSchemaRunner(
        this IServiceCollection services,
        Action<SchemaRunnerOptions>? configure = null
    )
    {
        Argument.IsNotNull(services);

        var options = services.AddOptions<SchemaRunnerOptions>();

        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton(static sp => new SchemaRunner(
            sp.GetServices<SchemaContribution>(),
            sp.GetService<ILogger<SchemaRunner>>(),
            sp.GetService<TimeProvider>(),
            sp.GetRequiredService<IOptions<SchemaRunnerOptions>>().Value.LockTimeout,
            sp.GetRequiredService<IOptions<SchemaRunnerOptions>>().Value.CommandTimeout
        ));
        services.AddInitializerHostedService<SchemaRunnerInitializer>();
        services.AddHeadlessStatefulResourceSource<SchemaResourceSource>();

        return services;
    }
}
