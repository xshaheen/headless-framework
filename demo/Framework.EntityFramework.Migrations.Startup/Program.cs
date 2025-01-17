// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Foundatio.Messaging;
using Framework.Api;
using Framework.Caching;
using Framework.Messaging;
using Framework.Permissions;
using Framework.Permissions.Storage.EntityFramework;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Cache;
using Framework.ResourceLocks.RegularLocks;
using Microsoft.EntityFrameworkCore;
using Savorboard.CAP.InMemoryMessageQueue;
using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;
using IMessageBus = Framework.Messaging.IMessageBus;

// To add a migration use:
// dotnet ef migrations add InitialMigration -p .\demo\Framework.EntityFramework.Migrations.Startup

// To generate the script use:
// dotnet ef migrations script --idempotent -s .\demo\Framework.EntityFramework.Migrations.Startup -o .\postgre-init.sql

// To generate the bundler use:
// dotnet ef migrations bundle --idempotent -p .\demo\Framework.EntityFramework.Migrations.Startup -o .\postgre-init.exe

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseDefaultServiceProvider(
    (_, options) =>
    {
        options.ValidateOnBuild = true;
        options.ValidateScopes = true;
    }
);

builder.AddFrameworkApiServices();

addInMemoryResourceLock(builder.Services);

builder.Services.AddCapDistributedMessaging(options =>
{
    options.UseInMemoryStorage();
    options.UseInMemoryMessageQueue();
});

builder
    .Services.AddPermissionsManagementCore()
    .AddPermissionsManagementEntityFrameworkStorage(options =>
    {
        options.UseNpgsql(
            "Host=localhost;Database=Framework;Username=postgres;Password=postgres",
            b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName)
        );
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
    services.AddSingleton<IMessageBus, MessageBusFoundatioAdapter>();

    services.AddResourceLock(
        provider => provider.GetRequiredService<IResourceLockStorage>(),
        provider => provider.GetRequiredService<IMessageBus>()
    );
}
