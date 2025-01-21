// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.Redis;

[PublicAPI]
public static class AddRedisBlobExtensions
{
    public static IServiceCollection AddRedisBlobStorage(
        this IServiceCollection services,
        Action<RedisBlobStorageOptions, IServiceProvider> setupAction
    )
    {
        services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddRedisBlobStorage(
        this IServiceCollection services,
        Action<RedisBlobStorageOptions> setupAction
    )
    {
        services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddRedisBlobStorage(this IServiceCollection services, IConfigurationSection config)
    {
        services.Configure<RedisBlobStorageOptions, RedisBlobStorageOptionsValidator>(config);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
        services.AddSingleton<IBlobStorage, RedisBlobStorage>();

        return services;
    }
}
