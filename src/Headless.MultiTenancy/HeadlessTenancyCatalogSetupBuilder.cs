// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.MultiTenancy;

/// <summary>
/// Fluent builder passed to the <c>HeadlessTenancyBuilder.Catalog(...)</c> configuration delegate; used
/// to configure <see cref="TenantCatalogOptions"/> and register exactly one storage provider extension.
/// </summary>
[PublicAPI]
public sealed class HeadlessTenancyCatalogSetupBuilder
{
    internal HeadlessTenancyCatalogSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<ITenantCatalogStorageOptionsExtension> Extensions { get; } = [];

    /// <summary>Applies a configuration delegate to the shared <see cref="TenantCatalogOptions"/>.</summary>
    /// <param name="configure">The delegate that mutates <see cref="TenantCatalogOptions"/>.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessTenancyCatalogSetupBuilder Configure(Action<TenantCatalogOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<TenantCatalogOptions, TenantCatalogOptionsValidator>(configure);

        return this;
    }

    /// <summary>Registers a storage provider extension that contributes its own services during the build phase.</summary>
    /// <param name="extension">The extension to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(ITenantCatalogStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
