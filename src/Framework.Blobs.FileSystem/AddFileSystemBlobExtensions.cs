// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.FileSystem;

[PublicAPI]
public static class AddFileSystemBlobExtensions
{
    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        Action<FileSystemBlobStorageOptions, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

        return _AddBaseServices(services);
    }

    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        Action<FileSystemBlobStorageOptions> setupAction
    )
    {
        services.ConfigureSingleton<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

        return _AddBaseServices(services);
    }

    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        IConfigurationSection config
    )
    {
        services.ConfigureSingleton<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(config);

        return _AddBaseServices(services);
    }

    private static IServiceCollection _AddBaseServices(IServiceCollection builder)
    {
        builder.AddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
        builder.AddSingleton<IBlobStorage, FileSystemBlobStorage>();

        return builder;
    }
}
