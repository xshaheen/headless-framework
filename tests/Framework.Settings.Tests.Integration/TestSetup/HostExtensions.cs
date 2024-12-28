// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Caching;
using Framework.ResourceLocks.Local;
using Framework.Settings;
using Framework.Settings.Seeders;
using Framework.Settings.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.TestSetup;

public static class HostExtensions
{
    public static IServiceCollection ConfigureSettingsServices(
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
        services.AddInMemoryCache();
        services.AddLocalResourceLock();

        services
            .AddSettingsManagementCore()
            .AddSettingsManagementEntityFrameworkStorage(options =>
            {
                options.UseNpgsql(postgreConnectionString);
            });

        services.RemoveHostedService<SettingsInitializationBackgroundService>();

        return services;
    }
}
