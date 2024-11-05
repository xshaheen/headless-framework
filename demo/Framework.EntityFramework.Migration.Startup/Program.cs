using Framework.Api;
using Framework.Caching;
using Framework.ResourceLocks.Local;
using Framework.Settings;
using Framework.Settings.Storage.EntityFramework;
using Microsoft.EntityFrameworkCore;

// To add a migration use:
// dotnet ef migrations add InitialMigration -s .\demo\Framework.EntityFramework.Migration.Startup -o .\Migrations -p .\src\Framework.Settings.Storage.EntityFramework

var builder = WebApplication.CreateBuilder(args);

builder.AddFrameworkApiServices();
builder.Services.AddInMemoryCache();
builder.Services.AddLocalResourceLock();

builder
    .Services.AddSettingsManagementCore()
    .AddSettingsManagementEntityFrameworkStorage(options =>
    {
        options.UseSqlite("Data Source=Settings.db");
    });

await builder.Build().RunAsync();
