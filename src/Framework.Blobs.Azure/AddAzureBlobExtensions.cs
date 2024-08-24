using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.Azure;

public static class AddAzureBlobExtensions
{
    public static IHostApplicationBuilder AddAzureBlobStorage(
        this IHostApplicationBuilder builder,
        string configSectionName = "Azure:Blobs"
    )
    {
        var section = builder.Configuration.GetSection(configSectionName);
        builder.Services.ConfigureSingleton<AzureStorageSettings, AzureStorageSettingsValidator>(section);
        _AddCoreServices(builder);

        return builder;
    }

    public static IHostApplicationBuilder AddAzureBlobStorage(
        this IHostApplicationBuilder builder,
        Action<AzureStorageSettings> configureOptions
    )
    {
        builder.Services.ConfigureSingleton<AzureStorageSettings, AzureStorageSettingsValidator>(configureOptions);
        _AddCoreServices(builder);

        return builder;
    }

    private static void _AddCoreServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, AzureBlobStorage>();
    }
}
