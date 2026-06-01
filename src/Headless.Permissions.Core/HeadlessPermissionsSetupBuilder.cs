// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Permissions.Models;
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

    internal IList<IPermissionsStorageOptionsExtension> Extensions { get; } = new List<IPermissionsStorageOptionsExtension>();

    public HeadlessPermissionsSetupBuilder ConfigureStorage(Action<PermissionsStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        configure(StorageOptions);

        return this;
    }

    public HeadlessPermissionsSetupBuilder ConfigureManagement(Action<PermissionManagementOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(configure);

        return this;
    }

    public HeadlessPermissionsSetupBuilder ConfigureManagement(
        Action<PermissionManagementOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(configure);

        return this;
    }

    public void RegisterExtension(IPermissionsStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
