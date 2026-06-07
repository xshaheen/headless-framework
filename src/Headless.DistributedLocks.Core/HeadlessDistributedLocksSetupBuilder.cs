// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks;

[PublicAPI]
public sealed class HeadlessDistributedLocksSetupBuilder
{
    internal HeadlessDistributedLocksSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal IList<IDistributedLocksOptionsExtension> Extensions { get; } =
        new List<IDistributedLocksOptionsExtension>();

    public HeadlessDistributedLocksSetupBuilder ConfigureOptions(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(configuration);

        return this;
    }

    public HeadlessDistributedLocksSetupBuilder ConfigureOptions(Action<DistributedLockOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(configure);

        return this;
    }

    public HeadlessDistributedLocksSetupBuilder ConfigureOptions(
        Action<DistributedLockOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<DistributedLockOptions, DistributedLockOptionsValidator>(configure);

        return this;
    }

    public void RegisterExtension(IDistributedLocksOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
