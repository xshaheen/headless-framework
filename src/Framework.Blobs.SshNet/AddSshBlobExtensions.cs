// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Blobs.SshNet;

[PublicAPI]
public static class AddSshBlobExtensions
{
    public static IServiceCollection AddSshBlobStorage(
        this IServiceCollection services,
        Action<SshBlobStorageOptions, IServiceProvider> setupAction
    )
    {
        services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddSshBlobStorage(
        this IServiceCollection services,
        Action<SshBlobStorageOptions> setupAction
    )
    {
        services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);

        return _AddCore(services);
    }

    public static IServiceCollection AddSshBlobStorage(this IServiceCollection services, IConfigurationSection config)
    {
        services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(config);

        return _AddCore(services);
    }

    private static IServiceCollection _AddCore(IServiceCollection services)
    {
        services.TryAddSingleton<IBlobNamingNormalizer, CrossOsNamingNormalizer>();
        services.AddSingleton<IBlobStorage, SshBlobStorage>();

        return services;
    }
}
