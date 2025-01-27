// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Hosting.Seeders;

public sealed class DbMigrationSeeder<TDbContext>(IServiceProvider provider) : ISeeder
    where TDbContext : DbContext
{
    public async ValueTask SeedAsync()
    {
        await provider.MigrateDbContextAsync<TDbContext>();
    }
}
