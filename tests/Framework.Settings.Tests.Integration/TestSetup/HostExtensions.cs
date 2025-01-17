// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Caching;
using Framework.Messaging;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Framework.Settings;
using Framework.Settings.Seeders;
using Framework.Settings.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
using IMessageBus = Framework.Messaging.IMessageBus;

namespace Tests.TestSetup;

public static class HostExtensions
{
    public static IServiceCollection ConfigureSettingsServices(
        this IServiceCollection services,
        string postgreConnectionString
    )
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        services._AddInMemoryResourceLock();

        services
            .AddSettingsManagementCore()
            .AddSettingsManagementEntityFrameworkStorage(options => options.UseNpgsql(postgreConnectionString));

        services.RemoveHostedService<SettingsInitializationBackgroundService>();

        return services;
    }

    private static void _AddInMemoryResourceLock(this IServiceCollection services)
    {
        services.AddInMemoryCache();
        services.AddSingleton<IFoundatioMessageBus>(_ => new InMemoryMessageBus(o => o.Topic("test-lock")));
        services.AddSingleton<IMessageBus, MessageBusFoundatioAdapter>();

        services.AddResourceLock(
            provider => new CacheResourceLockStorage(provider.GetRequiredService<ICache>()),
            provider => provider.GetRequiredService<IMessageBus>(),
            (options, _) => options.KeyPrefix = "test"
        );
    }
}
