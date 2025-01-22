// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Definitions;
using Framework.Permissions.Grants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Permissions;

[PublicAPI]
public static class AddPermissionsManagementStorageExtensions
{
    public static IServiceCollection AddPermissionsManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
    {
        return services.AddPermissionsManagementEntityFrameworkStorage<PermissionsDbContext>(setupAction);
    }

    public static IServiceCollection AddPermissionsManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
    {
        return services.AddPermissionsManagementEntityFrameworkStorage<PermissionsDbContext>(setupAction);
    }

    public static IServiceCollection AddPermissionsManagementEntityFrameworkStorage<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
        where TContext : DbContext, IPermissionsDbContext
    {
        services.AddPooledDbContextFactory<TContext>(setupAction);

        services.AddSingleton<IPermissionGrantRepository, EfPermissionGrantRepository<TContext>>();

        services.AddSingleton<
            IPermissionDefinitionRecordRepository,
            EfPermissionDefinitionRecordRepository<TContext>
        >();

        return services;
    }

    public static IServiceCollection AddPermissionsManagementEntityFrameworkStorage<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
        where TContext : DbContext, IPermissionsDbContext
    {
        services.AddPooledDbContextFactory<TContext>(setupAction);

        services.AddSingleton<IPermissionGrantRepository, EfPermissionGrantRepository<TContext>>();

        services.AddSingleton<
            IPermissionDefinitionRecordRepository,
            EfPermissionDefinitionRecordRepository<TContext>
        >();

        return services;
    }
}
