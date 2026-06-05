// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Features.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

[PublicAPI]
public sealed class HeadlessFeaturesSetupBuilder
{
    internal HeadlessFeaturesSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal FeaturesStorageOptions StorageOptions { get; } = new();

    internal IList<IFeaturesStorageOptionsExtension> Extensions { get; } = new List<IFeaturesStorageOptionsExtension>();

    public HeadlessFeaturesSetupBuilder ConfigureStorage(Action<FeaturesStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        configure(StorageOptions);

        return this;
    }

    public HeadlessFeaturesSetupBuilder ConfigureManagement(Action<FeatureManagementOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(configure);

        return this;
    }

    public HeadlessFeaturesSetupBuilder ConfigureManagement(
        Action<FeatureManagementOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<FeatureManagementOptions, FeatureManagementOptionsValidator>(configure);

        return this;
    }

    public void RegisterExtension(IFeaturesStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
