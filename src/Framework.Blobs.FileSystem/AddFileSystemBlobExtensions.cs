using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.FileSystem;

public static class AddFileSystemBlobExtensions
{
    public static IHostApplicationBuilder AddFileSystemBlobStorage(
        this IHostApplicationBuilder builder,
        Action<FileSystemBlobStorageSettings> configure
    )
    {
        builder.Services.ConfigureSingleton<FileSystemBlobStorageSettings, FileSystemBlobStorageSettingsValidator>(
            configure
        );
        _AddBaseServices(builder);

        return builder;
    }

    public static IHostApplicationBuilder AddFileSystemBlobStorage(
        this IHostApplicationBuilder builder,
        IConfigurationSection configuration
    )
    {
        builder.Services.ConfigureSingleton<FileSystemBlobStorageSettings, FileSystemBlobStorageSettingsValidator>(
            configuration
        );
        _AddBaseServices(builder);

        return builder;
    }

    private static void _AddBaseServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBlobNamingNormalizer, FileSystemBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, FileSystemBlobStorage>();
    }
}
