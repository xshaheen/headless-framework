// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination;

/// <summary>
/// Fluent builder passed to the <c>AddHeadlessCoordination</c> delegate. Used to bind
/// <see cref="CoordinationOptions"/> and select exactly one backing store provider via a
/// <c>Use*</c> extension (for example <c>UsePostgreSql</c>, <c>UseRedis</c>, <c>UseSqlServer</c>).
/// </summary>
[PublicAPI]
public sealed class HeadlessCoordinationSetupBuilder
{
    internal HeadlessCoordinationSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<ICoordinationProviderOptionsExtension> Extensions { get; } =
        new List<ICoordinationProviderOptionsExtension>();

    /// <summary>Binds <see cref="CoordinationOptions"/> from the supplied <see cref="IConfiguration"/> section.</summary>
    /// <param name="configuration">The configuration section to bind from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public HeadlessCoordinationSetupBuilder Configure(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configuration);

        return this;
    }

    /// <summary>Configures <see cref="CoordinationOptions"/> using the supplied delegate.</summary>
    /// <param name="configure">Delegate that mutates the options instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessCoordinationSetupBuilder Configure(Action<CoordinationOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configure);

        return this;
    }

    /// <summary>Configures <see cref="CoordinationOptions"/> using the supplied delegate with access to the DI container.</summary>
    /// <param name="configure">Delegate that mutates the options instance using the service provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessCoordinationSetupBuilder Configure(Action<CoordinationOptions, IServiceProvider> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configure);

        return this;
    }

    /// <summary>
    /// Registers a provider extension. Called internally by each <c>Use*</c> extension method; not
    /// intended for direct use by application code.
    /// </summary>
    /// <param name="extension">The provider extension to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(ICoordinationProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
