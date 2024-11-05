// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks.Local;
using Framework.Settings;
using Framework.Settings.Helpers;
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
        services.AddSingleton<IUniqueLongGenerator>(new SnowFlakIdUniqueLongGenerator(1));
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
