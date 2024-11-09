// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Permissions.Definitions;
using Framework.Permissions.Grants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Permissions.Storage.EntityFramework;

[PublicAPI]
public static class AddPermissionsManagementEntityFrameworkStorageExtensions
{
    public static IServiceCollection AddPermissionsManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction
    )
    {
        services.AddSingleton<IPermissionGrantRepository, EfPermissionGrantRepository>();
        services.AddSingleton<IPermissionDefinitionRecordRepository, EfPermissionDefinitionRecordRepository>();
        services.AddPooledDbContextFactory<PermissionsDbContext>(optionsAction);

        return services;
    }
}
