// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Caching;
using Framework.Messaging;
using Framework.Permissions;
using Framework.Permissions.Seeders;
using Framework.Permissions.Storage.EntityFramework;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Framework.ResourceLocks.RegularLocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
using IMessageBus = Framework.Messaging.IMessageBus;

namespace Tests.TestSetup;

public static class HostExtensions
{
    public static IServiceCollection ConfigurePermissionsServices(
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
        services.AddSingleton(Substitute.For<ICurrentPrincipalAccessor>());
        services.AddSingleton(Substitute.For<IDistributedMessagePublisher>());

        services._AddInMemoryResourceLock();

        services
            .AddPermissionsManagementCore()
            .AddPermissionsManagementEntityFrameworkStorage(options => options.UseNpgsql(postgreConnectionString));

        services.RemoveHostedService<PermissionsInitializationBackgroundService>();

        return services;
    }

    private static void _AddInMemoryResourceLock(this IServiceCollection services)
    {
        // Cache
        services.AddInMemoryCache();
        services.AddSingleton<IResourceLockStorage, CacheResourceLockStorage>();
        // MessageBus
        services.AddSingleton<IFoundatioMessageBus>(_ => new InMemoryMessageBus(o => o.Topic("test-lock")));
        services.AddSingleton<IMessageBus, MessageBusFoundatioAdapter>();

        services.AddResourceLock(
            provider => provider.GetRequiredService<IResourceLockStorage>(),
            provider => provider.GetRequiredService<IMessageBus>(),
            (options, _) => options.KeyPrefix = "resource-locks:"
        );
    }
}
