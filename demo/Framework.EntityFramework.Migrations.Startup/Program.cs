using System.Reflection;
using Framework.Api;
using Framework.Caching;
using Framework.Features;
using Framework.Features.Storage.EntityFramework;
using Framework.ResourceLocks.Local;
using Microsoft.EntityFrameworkCore;

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

builder
    .Services.AddFeaturesManagementCore()
    .AddFeaturesManagementEntityFrameworkStorage(options =>
    {
        options.UseNpgsql(
            "Host=localhost;Database=Framework;Username=postgres;Password=postgres",
            b => b.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName)
        );
    });

await builder.Build().RunAsync();
