using Headless.Ticker.DbContextFactory;
using Headless.Ticker.Entities;
using Headless.Ticker.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// TickerQ setup with SQLite operational store (file-based)
builder.Services.AddTickerQ(options =>
{
    options.AddOperationalStore(efOptions =>
    {
        efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
        {
            dbOptions.UseSqlite(
                "Data Source=tickerq-webapi.db",
                b => b.MigrationsAssembly("Headless.Ticker.Sample.WebApi")
            );
        });
    });
});

var app = builder.Build();

// Ensure TickerQ operational store schema is applied
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    await db.Database.MigrateAsync();
}

// Activate TickerQ job processor (mirrors docs' minimal setup)
app.UseTickerQ();

// Minimal endpoint to schedule the sample job
app.MapPost(
    "/schedule-sample",
    async (ITimeTickerManager<TimeTickerEntity> manager) =>
    {
        var result = await manager.AddAsync(
            new TimeTickerEntity { Function = "WebApiSample_HelloWorld", ExecutionTime = DateTime.UtcNow.AddSeconds(5) }
        );

        return Results.Ok(new { result.Result!.Id, ScheduledFor = result.Result.ExecutionTime });
    }
);
await app.RunAsync();

// Simple sample job
