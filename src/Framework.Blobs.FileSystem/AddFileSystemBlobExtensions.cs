// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.FileSystem;

[PublicAPI]
public static class AddFileSystemBlobExtensions
{
    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        Action<FileSystemBlobStorageSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<FileSystemBlobStorageSettings, FileSystemBlobStorageSettingsValidator>(setupAction);

        return _AddBaseServices(services);
    }

    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        Action<FileSystemBlobStorageSettings> setupAction
    )
    {
        services.ConfigureSingleton<FileSystemBlobStorageSettings, FileSystemBlobStorageSettingsValidator>(setupAction);

        return _AddBaseServices(services);
    }

    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        IConfigurationSection config
    )
    {
        services.ConfigureSingleton<FileSystemBlobStorageSettings, FileSystemBlobStorageSettingsValidator>(config);

        return _AddBaseServices(services);
    }

    private static IServiceCollection _AddBaseServices(IServiceCollection builder)
    {
        builder.AddSingleton<IBlobNamingNormalizer, FileSystemBlobNamingNormalizer>();
        builder.AddSingleton<IBlobStorage, FileSystemBlobStorage>();

        return builder;
    }
}
