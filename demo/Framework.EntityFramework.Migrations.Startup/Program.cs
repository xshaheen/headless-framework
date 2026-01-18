// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Foundatio.Messaging;
using Framework.Api;
using Framework.Caching;
using Framework.Domain;
using Framework.Features;
using Framework.Messaging;
using Framework.Permissions;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Framework.ResourceLocks.RegularLocks;
using Framework.Settings;
using Microsoft.EntityFrameworkCore;
using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
using IMessageBus = Framework.Messaging.IMessageBus;

// To add a migration use:
// dotnet ef migrations add InitialMigration -p .\demo\Framework.EntityFramework.Migrations.Startup --context FeaturesDbContext

// To generate the script use:
// dotnet ef migrations script --idempotent -s .\demo\Framework.EntityFramework.Migrations.Startup -o .\postgre-init.sql --context FeaturesDbContext

// To generate the bundler use:
// dotnet ef migrations bundle --idempotent -p .\demo\Framework.EntityFramework.Migrations.Startup -o .\postgre-init.exe --context FeaturesDbContext

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

addInMemoryResourceLock(builder.Services);

builder.Services.AddCapDistributedMessaging(options =>
{
    options.UseInMemoryStorage();
    options.UseInMemoryMessageQueue();
});

const string connectionString = "Host=localhost;Database=Framework;Username=postgres;Password=postgres";

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

static void addInMemoryResourceLock(IServiceCollection services)
{
    // Cache
    services.AddInMemoryCache();
    services.AddSingleton<IResourceLockStorage, CacheResourceLockStorage>();
    // MessageBus
    services.AddSingleton<IFoundatioMessageBus>(_ => new InMemoryMessageBus(o => o.Topic("test-lock")));
    services.AddMessageBusFoundatioAdapter();

    services.AddResourceLock(
        provider => provider.GetRequiredService<IResourceLockStorage>(),
        provider => provider.GetRequiredService<IMessageBus>()
    );
}
