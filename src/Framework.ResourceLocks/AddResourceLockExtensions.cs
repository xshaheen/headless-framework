// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Messaging;
using Framework.ResourceLocks.RegularLocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Framework.ResourceLocks;

[PublicAPI]
public static class AddResourceLockExtensions
{
    public static void AddResourceLockCore(
        this IServiceCollection services,
        Func<IServiceProvider, IResourceLockStorage> storageSetupAction,
        Func<IServiceProvider, IMessageBus> busSetupAction,
        Action<ResourceLockOptions, IServiceProvider>? optionSetupAction = null
    )
    {
        if (optionSetupAction is null)
        {
            services.AddSingletonOptions<ResourceLockOptions>();
        }
        else
        {
            services.ConfigureSingleton(optionSetupAction);
        }

        services.AddSingleton(storageSetupAction);
        services.AddSingleton(busSetupAction);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));

        services.AddSingleton<IResourceLockProvider>(provider => new ResourceLockProvider(
            storageSetupAction(provider),
            busSetupAction(provider),
            provider.GetRequiredService<ResourceLockOptions>(),
            provider.GetRequiredService<ILongIdGenerator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ResourceLockProvider>>()
        ));
    }

    public static IServiceCollection AddThrottlingResourceLock(
        this IServiceCollection services,
        ThrottlingResourceLockOptions options,
        Func<IServiceProvider, IThrottlingResourceLockStorage> setupAction
    )
    {
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IThrottlingResourceLockProvider>(provider => new ThrottlingResourceLockProvider(
            setupAction(provider),
            options,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ThrottlingResourceLockProvider>>()
        ));

        return services;
    }

    public static IServiceCollection AddKeyedThrottlingResourceLock(
        this IServiceCollection services,
        string key,
        ThrottlingResourceLockOptions options,
        Func<IServiceProvider, IThrottlingResourceLockStorage> setupAction
    )
    {
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        services.AddKeyedSingleton<IThrottlingResourceLockProvider>(
            key,
            provider => new ThrottlingResourceLockProvider(
                setupAction(provider),
                options,
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<ThrottlingResourceLockProvider>>()
            )
        );

        return services;
    }
}
