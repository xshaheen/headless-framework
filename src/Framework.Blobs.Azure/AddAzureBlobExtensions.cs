// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.Azure;

public static class AddAzureBlobExtensions
{
    public static IServiceCollection AddAzureBlobStorage(
        this IServiceCollection services,
        Action<AzureStorageSettings, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<AzureStorageSettings, AzureStorageSettingsValidator>(setupAction);
        _AddCoreServices(services);

        return services;
    }

    public static IServiceCollection AddAzureBlobStorage(
        this IServiceCollection services,
        Action<AzureStorageSettings> setupAction
    )
    {
        services.ConfigureSingleton<AzureStorageSettings, AzureStorageSettingsValidator>(setupAction);
        _AddCoreServices(services);

        return services;
    }

    public static IServiceCollection AddAzureBlobStorage(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureSingleton<AzureStorageSettings, AzureStorageSettingsValidator>(config);
        _AddCoreServices(services);

        return services;
    }

    private static void _AddCoreServices(IServiceCollection services)
    {
        services.AddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
        services.AddSingleton<IBlobStorage, AzureBlobStorage>();
    }
}
