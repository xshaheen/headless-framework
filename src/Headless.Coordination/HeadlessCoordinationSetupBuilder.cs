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

    internal IList<ICoordinationProviderOptionsExtension> Extensions { get; } = [];

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
    /// Configures <see cref="CoordinationStorageOptions"/> (the schema the membership tables live in) using the
    /// supplied delegate. Applies to every relational provider; providers with no schema concept ignore it.
    /// </summary>
    /// <param name="configure">Delegate that mutates the storage options instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessCoordinationSetupBuilder ConfigureStorage(Action<CoordinationStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        // No validator here: the selected provider attaches the one that knows its dialect, so the same
        // schema string is checked against PostgreSQL or SQL Server identifier rules, never both.
        Services.Configure(configure);

        return this;
    }

    /// <summary>
    /// Binds <see cref="CoordinationStorageOptions"/> from the supplied <see cref="IConfiguration"/> section —
    /// pass the <c>Headless:Coordination:Storage</c> section to bind <c>Headless:Coordination:Storage:Schema</c>.
    /// </summary>
    /// <param name="configuration">The configuration section to bind from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public HeadlessCoordinationSetupBuilder ConfigureStorage(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<CoordinationStorageOptions>(configuration);

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
