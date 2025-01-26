// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Hosting.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.EntityFramework.Seeders;

[PublicAPI]
public static class AddDbMigrationSeederExtensions
{
    public static void AddDbMigrationSeeder<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddSeeder<DbMigrationSeeder<TContext>>();
    }
}
