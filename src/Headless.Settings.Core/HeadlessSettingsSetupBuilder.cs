// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings;

/// <summary>Fluent builder passed to the <c>AddHeadlessSettings</c> configuration delegate; used to configure storage, management options, and storage provider extensions.</summary>
[PublicAPI]
public sealed class HeadlessSettingsSetupBuilder
{
    internal HeadlessSettingsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal SettingsStorageOptions StorageOptions { get; } = new();

    internal IList<ISettingsStorageOptionsExtension> Extensions { get; } = new List<ISettingsStorageOptionsExtension>();

    /// <summary>Applies a configuration delegate to the shared <see cref="SettingsStorageOptions"/>.</summary>
    /// <param name="configure">The delegate that mutates <see cref="SettingsStorageOptions"/>.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessSettingsSetupBuilder ConfigureStorage(Action<SettingsStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        configure(StorageOptions);

        return this;
    }

    /// <summary>Applies a configuration delegate to the registered <see cref="SettingManagementOptions"/>.</summary>
    /// <param name="configure">The delegate that mutates <see cref="SettingManagementOptions"/>.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessSettingsSetupBuilder ConfigureManagement(Action<SettingManagementOptions> configure)
    {
        Argument.IsNotNull(configure);

        Services.Configure<SettingManagementOptions, SettingManagementOptionsValidator>(configure);

        return this;
    }

    /// <summary>Applies a configuration delegate that receives the <see cref="IServiceProvider"/> to the registered <see cref="SettingManagementOptions"/>.</summary>
    /// <param name="configure">The delegate that mutates <see cref="SettingManagementOptions"/> using the service provider.</param>
    /// <returns>The same builder instance to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public HeadlessSettingsSetupBuilder ConfigureManagement(
        Action<SettingManagementOptions, IServiceProvider> configure
    )
    {
        Argument.IsNotNull(configure);

        Services.Configure<SettingManagementOptions, SettingManagementOptionsValidator>(configure);

        return this;
    }

    /// <summary>Registers a storage provider extension that contributes its own services during the build phase.</summary>
    /// <param name="extension">The extension to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    public void RegisterExtension(ISettingsStorageOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        Extensions.Add(extension);
    }
}
