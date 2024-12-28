// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Caching;
using Framework.Messaging;
using Framework.Permissions;
using Framework.Permissions.Seeders;
using Framework.Permissions.Storage.EntityFramework;
using Framework.ResourceLocks.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.TestSetup;

public static class HostExtensions
{
    public static IServiceCollection ConfigurePermissionsServices(
        this IServiceCollection services,
        string postgreConnectionString
    )
    {
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ILongIdGenerator>(new SnowFlakIdLongIdGenerator(1));
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        services.AddSingleton(Substitute.For<ICurrentPrincipalAccessor>());
        services.AddSingleton(Substitute.For<IDistributedMessagePublisher>());

        services.AddInMemoryCache();
        services.AddLocalResourceLock();

        services
            .AddPermissionsManagementCore()
            .AddPermissionsManagementEntityFrameworkStorage(options =>
            {
                options.UseNpgsql(postgreConnectionString);
            });

        services.RemoveHostedService<PermissionsInitializationBackgroundService>();

        return services;
    }
}
