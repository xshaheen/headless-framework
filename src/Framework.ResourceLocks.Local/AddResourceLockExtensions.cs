// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Framework.ResourceLocks.Local;

[PublicAPI]
public static class AddResourceLockExtensions
{
    public static IServiceCollection AddLocalThrottlingResourceLock(
        this IServiceCollection services,
        ThrottlingResourceLockOptions options
    )
    {
        services.AddSingleton<IResourceThrottlingLockProvider>(provider => new ResourceThrottlingLockProvider(
            new LocalResourceThrottlingLockStorage(),
            options,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ResourceThrottlingLockProvider>>()
        ));

        return services;
    }

    public static IServiceCollection AddKeyedLocalThrottlingResourceLock(
        this IServiceCollection services,
        string key,
        ThrottlingResourceLockOptions options
    )
    {
        services.AddKeyedSingleton<IResourceThrottlingLockProvider>(
            key,
            provider => new ResourceThrottlingLockProvider(
                new LocalResourceThrottlingLockStorage(),
                options,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ResourceThrottlingLockProvider>>()
            )
        );

        return services;
    }

    public static IServiceCollection AddLocalResourceLock(
        this IServiceCollection services,
        Action<ResourceLockOptions, IServiceProvider> setupAction
    )
    {
        services.ConfigureSingleton(setupAction);
        services._AddResourceLockCore();

        return services;
    }

    public static IServiceCollection AddLocalResourceLock(
        this IServiceCollection services,
        Action<ResourceLockOptions>? setupAction = null
    )
    {
        if (setupAction is null)
        {
            services.AddSingletonOptions<ResourceLockOptions>();
        }
        else
        {
            services.ConfigureSingleton(setupAction);
        }

        services._AddResourceLockCore();

        return services;
    }

    private static void _AddResourceLockCore(this IServiceCollection services)
    {
        services.AddSingleton<IResourceLockProvider, LocalResourceLockProvider>();
    }
}
