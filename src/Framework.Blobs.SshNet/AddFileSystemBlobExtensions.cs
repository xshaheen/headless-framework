using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.SshNet;

public static class AddFileSystemBlobExtensions
{
    public static IHostApplicationBuilder AddSshBlobStorage(
        this IHostApplicationBuilder builder,
        Action<SshBlobStorageSettings> configure
    )
    {
        builder.Services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(configure);
        _AddBaseServices(builder);

        return builder;
    }

    public static IHostApplicationBuilder AddSshBlobStorage(
        this IHostApplicationBuilder builder,
        IConfigurationSection configuration
    )
    {
        builder.Services.ConfigureSingleton<SshBlobStorageSettings, SshBlobStorageSettingsValidator>(configuration);
        _AddBaseServices(builder);

        return builder;
    }

    private static void _AddBaseServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBlobNamingNormalizer, SshBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, SshBlobStorage>();
    }
}
