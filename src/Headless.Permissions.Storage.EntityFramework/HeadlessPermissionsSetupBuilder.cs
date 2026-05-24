// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

[PublicAPI]
public sealed class HeadlessPermissionsSetupBuilder
{
    internal HeadlessPermissionsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal PermissionsStorageOptions StorageOptions { get; } = new();

    internal IList<IStorageOptionsExtension> Extensions { get; } = new List<IStorageOptionsExtension>();

    public HeadlessPermissionsSetupBuilder ConfigureStorage(Action<PermissionsStorageOptions> configure)
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
