using System.Reflection;
using Foundatio.Messaging;
using Framework.Api;
using Framework.Caching;
using Framework.Messaging;
using Framework.Permissions;
using Framework.Permissions.Storage.EntityFramework;
using Framework.ResourceLocks.Local;
using Microsoft.EntityFrameworkCore;
using Savorboard.CAP.InMemoryMessageQueue;

// To add a migration use:
// dotnet ef migrations add InitialMigration -p .\demo\Framework.EntityFramework.Migrations.Startup

// To generate the script use:
// dotnet ef migrations script -s .\demo\Framework.EntityFramework.Migrations.Startup -o .\postgre-init.sql

// To generate the bundler use:
// dotnet ef migrations bundle -p .\demo\Framework.EntityFramework.Migrations.Startup -o .\postgre-init.exe

var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddInMemoryCache();
builder.Services.AddLocalResourceLock();
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

await builder.Build().RunAsync();
