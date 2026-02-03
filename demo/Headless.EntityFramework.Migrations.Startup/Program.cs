// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Cache;
using Headless.Features;
using Headless.Permissions;
using Headless.Permissions.Storage.EntityFramework;
using Headless.Settings;
using Headless.Settings.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

// To add a migration use:
// dotnet ef migrations add InitialMigration -p .\demo\Headless.EntityFramework.Migrations.Startup --context FeaturesDbContext

// To generate the script use:
// dotnet ef migrations script --idempotent -s .\demo\Headless.EntityFramework.Migrations.Startup -o .\postgre-init.sql --context FeaturesDbContext

// To generate the bundler use:
// dotnet ef migrations bundle --idempotent -p .\demo\Headless.EntityFramework.Migrations.Startup -o .\postgre-init.exe --context FeaturesDbContext

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(
    (_, options) =>
    {
        options.ValidateOnBuild = true;
        options.ValidateScopes = true;
    }
);

builder.AddHeadlessApi(encryption =>
{
    encryption.DefaultPassPhrase = "DemoPassPhrase123456";
    encryption.InitVectorBytes = "DemoIV0123456789"u8.ToArray();
    encryption.DefaultSalt = "DemoSalt"u8.ToArray();
});

addInMemoryDistributedLock(builder.Services);

const string connectionString = "Host=localhost;Database=Headless;Username=postgres;Password=postgres";

builder
    .Services.AddPermissionsManagementCore()
    .AddPermissionsManagementDbContextStorage(options =>
    {
        options.UseNpgsql(connectionString, b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName));
    });

builder
    .Services.AddSettingsManagementCore(encryption =>
    {
        encryption.DefaultPassPhrase = "DemoPassPhrase123456";
        encryption.InitVectorBytes = "DemoIV0123456789"u8.ToArray();
        encryption.DefaultSalt = "DemoSalt"u8.ToArray();
    })
    .AddSettingsManagementDbContextStorage(options =>
    {
        options.UseNpgsql(connectionString, b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName));
    });

builder
    .Services.AddFeaturesManagementCore()
    .AddFeaturesManagementDbContextStorage(options =>
    {
        options.UseNpgsql(connectionString, b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName));
    });

var app = builder.Build();

await app.RunAsync();

return;

static void addInMemoryDistributedLock(IServiceCollection services)
{
    // Cache
    services.AddInMemoryCache();

    // Messages
    services.AddMessages(options =>
    {
        options.UseInMemoryMessageQueue();
        options.UseInMemoryStorage();
    });

    // Resource Locks
    services.AddDistributedLock<CacheDistributedLockStorage>();
}
