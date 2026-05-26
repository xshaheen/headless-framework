// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Storage;
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

    internal IList<IStorageOptionsExtension> Extensions { get; } = new List<IStorageOptionsExtension>();

    public HeadlessFeaturesSetupBuilder ConfigureStorage(Action<FeaturesStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        configure(StorageOptions);

        return this;
    }

    public void RegisterExtension(IStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
