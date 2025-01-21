// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.FileSystem;

[PublicAPI]
public static class AddFileSystemBlobExtensions
{
    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        Action<FileSystemBlobStorageOptions, IServiceProvider> setupAction
    )
    {
        services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

        return _AddBaseServices(services);
    }

    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        Action<FileSystemBlobStorageOptions> setupAction
    )
    {
        services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);

        return _AddBaseServices(services);
    }

    public static IServiceCollection AddFileSystemBlobStorage(
        this IServiceCollection services,
        IConfigurationSection config
    )
    {
        services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(config);

        return _AddBaseServices(services);
    }

    private static IServiceCollection _AddBaseServices(IServiceCollection builder)
    {
        builder.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
        builder.AddSingleton<IBlobStorage, FileSystemBlobStorage>();

        return builder;
    }
}
