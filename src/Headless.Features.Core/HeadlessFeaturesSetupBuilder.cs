// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

/// <summary>Builder passed to the <c>AddHeadlessFeatures</c> configure delegate; used to select a storage provider and tune management options.</summary>
/// <remarks>
/// Exactly one storage provider must be selected per registration (for example <c>UseEntityFramework</c>,
/// <c>UsePostgreSql</c>, or <c>UseSqlServer</c>). Call <c>ConfigureStorage</c> to override table
/// and schema names, and <c>ConfigureManagement</c> to tune caching, lock timeouts, or the dynamic
/// store toggle. Calling <c>AddHeadlessFeatures</c> a second time on the same <c>IServiceCollection</c>
/// is safe — the core services are registered only once.
/// </remarks>
[PublicAPI]
public sealed class HeadlessFeaturesSetupBuilder
{
    internal HeadlessFeaturesSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal FeaturesStorageOptions StorageOptions { get; } = new();

    internal IList<IFeaturesStorageOptionsExtension> Extensions { get; } = [];

    internal bool RegisterStartupInitializer { get; private set; } = true;

    /// <summary>
    /// Skips the hosted service that seeds static feature definitions into the store and pre-caches dynamic ones
    /// at startup. Use it on hosts that must not touch the store at startup, such as test hosts and read-only
    /// replicas; static definitions stay available in memory and every other registration is unchanged.
    /// </summary>
    /// <returns>The same builder for chaining.</returns>
    public HeadlessFeaturesSetupBuilder DisableStartupInitialization()
    {
        RegisterStartupInitializer = false;

        return this;
    }

    /// <summary>Applies <paramref name="configure"/> to the shared <see cref="FeaturesStorageOptions"/> (schema names, table names, etc.).</summary>
    /// <param name="configure">A delegate that mutates the storage options.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessFeaturesSetupBuilder ConfigureStorage(Action<FeaturesStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        configure(StorageOptions);

        return this;
    }

    /// <summary>
    /// Binds the shared <see cref="FeaturesStorageOptions"/> from <paramref name="configuration"/>; pass the
    /// <c>Headless:Features:Storage</c> section. Applied immediately, in call order with the delegate overload.
    /// </summary>
    /// <param name="configuration">The configuration section to bind from.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public HeadlessFeaturesSetupBuilder ConfigureStorage(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        configuration.Bind(StorageOptions);

        return this;
    }

    /// <summary>Registers a management options configuration delegate that is resolved at startup.</summary>
    /// <param name="configure">A delegate that mutates <see cref="FeatureManagementOptions"/>.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessFeaturesSetupBuilder ConfigureManagement(Action<FeatureManagementOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(configure);

        return this;
    }

    /// <summary>Registers a management options configuration delegate that receives the <see cref="IServiceProvider"/> for late-bound configuration.</summary>
    /// <param name="configure">A delegate that mutates <see cref="FeatureManagementOptions"/> using resolved services.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessFeaturesSetupBuilder ConfigureManagement(
        Action<FeatureManagementOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(configure);

        return this;
    }

    /// <summary>Adds a storage provider extension that contributes its services during the <c>AddHeadlessFeatures</c> pipeline.</summary>
    /// <param name="extension">The extension to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(IFeaturesStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
