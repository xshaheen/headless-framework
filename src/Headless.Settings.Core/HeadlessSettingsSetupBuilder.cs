// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings;

[PublicAPI]
public sealed class HeadlessSettingsSetupBuilder
{
    internal HeadlessSettingsSetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    internal SettingsStorageOptions StorageOptions { get; } = new();

    internal IList<IStorageOptionsExtension> Extensions { get; } = new List<IStorageOptionsExtension>();

    public HeadlessSettingsSetupBuilder ConfigureStorage(Action<SettingsStorageOptions> configure)
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
