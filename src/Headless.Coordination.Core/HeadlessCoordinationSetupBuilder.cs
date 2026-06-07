// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Coordination;

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

    public HeadlessCoordinationSetupBuilder Configure(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configuration);

        return this;
    }

    public HeadlessCoordinationSetupBuilder Configure(Action<CoordinationOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configure);

        return this;
    }

    public HeadlessCoordinationSetupBuilder Configure(Action<CoordinationOptions, IServiceProvider> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<CoordinationOptions, CoordinationOptionsValidator>(configure);

        return this;
    }

    public void RegisterExtension(ICoordinationProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
