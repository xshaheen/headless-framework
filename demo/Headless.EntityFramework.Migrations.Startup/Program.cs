// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.EntityFramework.Migrations.Startup;
using Headless.Features;
using Headless.Permissions;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

// To add a migration use:
// dotnet ef migrations add InitialMigration -p .\demo\Headless.EntityFramework.Migrations.Startup --context FeaturesMigrationDbContext

// To generate the script use:
// dotnet ef migrations script --idempotent -s .\demo\Headless.EntityFramework.Migrations.Startup -o .\postgre-init.sql --context FeaturesMigrationDbContext

// To generate the bundler use:
// dotnet ef migrations bundle --idempotent -p .\demo\Headless.EntityFramework.Migrations.Startup -o .\postgre-init.exe --context FeaturesMigrationDbContext

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(
    (_, options) =>
    {
        options.ValidateOnBuild = true;
        options.ValidateScopes = true;
    }
);

builder.AddHeadless(encryption =>
{
    encryption.DefaultPassPhrase = "DemoPassPhrase123456";
    encryption.DefaultSalt = "DemoSalt"u8.ToArray();
});

addRedisDistributedLock(builder.Services);

const string connectionString = "Host=localhost;Database=Headless;Username=postgres;Password=postgres";

builder.Services.AddDbContextFactory<PermissionsMigrationDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName))
);

builder.Services.AddHeadlessPermissions(setup =>
{
    setup.UseEntityFramework<PermissionsMigrationDbContext>();
});

builder.Services.AddDbContextFactory<SettingsMigrationDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName))
);

builder.Services.AddHeadlessSettings(setup =>
{
    setup.UseEntityFramework<SettingsMigrationDbContext>();
});

builder.Services.AddDbContextFactory<FeaturesMigrationDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName))
);

builder.Services.AddHeadlessFeatures(setup =>
{
    setup.UseEntityFramework<FeaturesMigrationDbContext>();
});

var app = builder.Build();

await app.RunAsync();

return;

static void addRedisDistributedLock(IServiceCollection services)
{
    // Redis connection (required by Headless.DistributedLocks.Redis)
    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));

    // Messages
    services.AddHeadlessMessaging(setup =>
    {
        setup.UseInMemory();
        setup.UseInMemoryStorage();
    });

    // Resource Locks
    services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
}
