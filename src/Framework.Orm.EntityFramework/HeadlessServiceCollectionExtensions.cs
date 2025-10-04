// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Orm.EntityFramework.ChangeTrackers;
using Framework.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Orm.EntityFramework;

[PublicAPI]
public static class HeadlessServiceCollectionExtensions
{
    public static IServiceCollection AddHeadlessDbContext<TDbContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? optionsAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
    )
        where TDbContext : HeadlessDbContext
    {
        return services.AddHeadlessDbContext<TDbContext>(
            (_, ob) => optionsAction?.Invoke(ob),
            contextLifetime,
            optionsLifetime
        );
    }

    public static IServiceCollection AddHeadlessDbContext<TDbContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
    )
        where TDbContext : HeadlessDbContext
    {
        services.TryAddSingleton<IClock, Clock>();
        services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.TryAddSingleton<ICurrentUser, NullCurrentUser>();

        services.TryAddSingleton<IHeadlessEntityModelProcessor, HeadlessEntityModelProcessor>();

        services.AddDbContext<TDbContext>(
            (serviceProvider, optionsBuilder) =>
            {
                optionsAction?.Invoke(serviceProvider, optionsBuilder);
                optionsBuilder.AddHeadlessDbContextOptionsExtension();
            },
            contextLifetime,
            optionsLifetime
        );

        return services;
    }
}
