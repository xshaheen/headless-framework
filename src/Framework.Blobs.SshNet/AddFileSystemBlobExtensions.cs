// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.SshNet;

public static class AddFileSystemBlobExtensions
{
    public static IServiceCollection AddSshBlobStorage(
        this IServiceCollection services,
        Action<SshBlobStorageSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(setupAction);
        _AddBaseServices(services);

        return services;
    }

    public static IServiceCollection AddSshBlobStorage(
        this IServiceCollection services,
        Action<SshBlobStorageSettings> setupAction
    )
    {
        services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(setupAction);
        _AddBaseServices(services);

        return services;
    }

    public static IServiceCollection AddSshBlobStorage(this IServiceCollection services, IConfigurationSection config)
    {
        services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(config);
        _AddBaseServices(services);

        return services;
    }

    private static void _AddBaseServices(IServiceCollection services)
    {
        services.AddSingleton<IBlobNamingNormalizer, SshBlobNamingNormalizer>();
        services.AddSingleton<IBlobStorage, SshBlobStorage>();
    }
}
