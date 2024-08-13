using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.SshNet;

public static class AddFileSystemBlobExtensions
{
    public static IHostApplicationBuilder AddSshBlobStorage(
        this IHostApplicationBuilder builder,
        Action<SshBlobStorageOptions> configure
    )
    {
        builder.Services.ConfigureSingleton<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(configure);
        _AddBaseServices(builder);

        return builder;
    }

    public static IHostApplicationBuilder AddSshBlobStorage(
        this IHostApplicationBuilder builder,
        IConfigurationSection configuration
    )
    {
        builder.Services.ConfigureSingleton<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(configuration);
        _AddBaseServices(builder);

        return builder;
    }

    private static void _AddBaseServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBlobNamingNormalizer, SshBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, SshBlobStorage>();
    }
}
