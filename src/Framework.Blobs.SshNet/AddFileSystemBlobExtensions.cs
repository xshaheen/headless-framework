// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.SshNet;

[PublicAPI]
public static class AddFileSystemBlobExtensions
{
    public static IServiceCollection AddSshBlobStorage(
        this IServiceCollection services,
        Action<SshBlobStorageSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddSshBlobStorage(
        this IServiceCollection services,
        Action<SshBlobStorageSettings> setupAction
    )
    {
        services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddSshBlobStorage(this IServiceCollection services, IConfigurationSection config)
    {
        services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(config);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IBlobNamingNormalizer, SshBlobNamingNormalizer>();
        services.AddSingleton<IBlobStorage, SshBlobStorage>();

        return services;
    }
}
