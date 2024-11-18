// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.Azure;

[PublicAPI]
public static class AddAzureBlobExtensions
{
    public static IServiceCollection AddAzureBlobStorage(
        this IServiceCollection services,
        Action<AzureStorageOptions, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddAzureBlobStorage(
        this IServiceCollection services,
        Action<AzureStorageOptions> setupAction
    )
    {
        services.ConfigureSingleton<AzureStorageOptions, AzureStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddAzureBlobStorage(this IServiceCollection services, IConfiguration config)
    {
        services.ConfigureSingleton<AzureStorageOptions, AzureStorageOptionsValidator>(config);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
        services.AddSingleton<IBlobStorage, AzureBlobStorage>();

        return services;
    }
}
