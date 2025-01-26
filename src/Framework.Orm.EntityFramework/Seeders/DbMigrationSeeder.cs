// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Hosting.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Orm.EntityFramework.Seeders;

public sealed class DbMigrationSeeder<TDbContext>(IServiceProvider provider) : ISeeder
    where TDbContext : DbContext
{
    public async ValueTask SeedAsync()
    {
        await provider.MigrateDbContextAsync<TDbContext>();
    }
}
