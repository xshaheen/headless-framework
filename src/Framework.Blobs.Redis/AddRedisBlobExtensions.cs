// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.Redis;

[PublicAPI]
public static class AddRedisBlobExtensions
{
    public static IServiceCollection AddRedisBlobStorage(
        this IServiceCollection services,
        Action<RedisBlobStorageOptions, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddRedisBlobStorage(
        this IServiceCollection services,
        Action<RedisBlobStorageOptions> setupAction
    )
    {
        services.ConfigureSingleton<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddRedisBlobStorage(this IServiceCollection services, IConfigurationSection config)
    {
        services.ConfigureSingleton<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(config);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.AddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
        services.AddSingleton<IBlobStorage, RedisBlobStorage>();

        return services;
    }
}
