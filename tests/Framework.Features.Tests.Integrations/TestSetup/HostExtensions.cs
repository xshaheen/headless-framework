// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Caching;
using Framework.Features;
using Framework.Features.Seeders;
using Framework.Features.Storage.EntityFramework;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.ResourceLocks.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.TestSetup;

public static class HostExtensions
{
    public static IServiceCollection ConfigureFeaturesServices(
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
        services.AddSingleton(Substitute.For<ICurrentPrincipalAccessor>());
        services.AddInMemoryCache();
        services.AddLocalResourceLock();

        services
            .AddFeaturesManagementCore()
            .AddFeaturesManagementEntityFrameworkStorage(options =>
            {
                options.UseNpgsql(postgreConnectionString);
            });

        services.RemoveHostedService<FeaturesInitializationBackgroundService>();

        return services;
    }
}
