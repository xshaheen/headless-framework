// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions;

/// <summary>
/// Fluent builder passed to the <c>configure</c> callback of
/// <see cref="SetupPermissions.AddHeadlessPermissions"/>. Use it to choose a storage provider
/// (<c>UseEntityFramework</c>, <c>UsePostgreSql</c>, or <c>UseSqlServer</c>) and to tune storage
/// and management options.
/// </summary>
[PublicAPI]
public sealed class HeadlessPermissionsSetupBuilder
{
    internal HeadlessPermissionsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal PermissionsStorageOptions StorageOptions { get; } = new();

    internal IList<IPermissionsStorageOptionsExtension> Extensions { get; } = [];

    /// <summary>
    /// Configures shared storage options (schema name, table names, startup initialization flag).
    /// Calls are applied in order and composed with provider-specific defaults.
    /// </summary>
    public HeadlessPermissionsSetupBuilder ConfigureStorage(Action<PermissionsStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        configure(StorageOptions);

        return this;
    }

    /// <summary>
    /// Configures <see cref="PermissionManagementOptions"/> via the DI pipeline (validated on startup).
    /// Composes with any <c>services.Configure&lt;PermissionManagementOptions&gt;(...)</c> calls made before or
    /// after, regardless of order.
    /// </summary>
    public HeadlessPermissionsSetupBuilder ConfigureManagement(Action<PermissionManagementOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(configure);

        return this;
    }

    /// <summary>
    /// Configures <see cref="PermissionManagementOptions"/> with access to the resolved
    /// <see cref="IServiceProvider"/>, for cases where option values come from other registered services.
    /// </summary>
    public HeadlessPermissionsSetupBuilder ConfigureManagement(
        Action<PermissionManagementOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<PermissionManagementOptions, PermissionManagementOptionsValidator>(configure);

        return this;
    }

    /// <summary>
    /// Registers a storage provider extension. Called by provider packages (e.g. <c>UseEntityFramework</c>);
    /// do not call directly in application code.
    /// </summary>
    public void RegisterExtension(IPermissionsStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
