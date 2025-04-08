// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Permissions;

[PublicAPI]
public static class AddPermissionsManagementStorageExtensions
{
    public static IServiceCollection AddPermissionsManagementDbContextStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
    {
        services.AddPooledDbContextFactory<PermissionsDbContext>(setupAction);
        services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>();

        return services;
    }

    public static IServiceCollection AddPermissionsManagementDbContextStorage(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
    {
        services.AddPooledDbContextFactory<PermissionsDbContext>(setupAction);
        services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>();

        return services;
    }

    public static IServiceCollection AddPermissionsManagementDbContextStorage<TContext>(
        this IServiceCollection services
    )
        where TContext : DbContext, IPermissionsDbContext
    {
        services.AddSingleton<IPermissionGrantRepository, EfPermissionGrantRepository<TContext>>();

        services.AddSingleton<
            IPermissionDefinitionRecordRepository,
            EfPermissionDefinitionRecordRepository<TContext>
        >();

        return services;
    }
}
