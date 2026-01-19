using Framework.Ticker.Base;
using Framework.Ticker.DbContextFactory;
using Framework.Ticker.DependencyInjection;
using Framework.Ticker.Utilities;
using Framework.Ticker.Utilities.Entities;
using Framework.Ticker.Utilities.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (context, services) =>
        {
            // Configure TickerQ with SQLite operational store (file-based)
            services.AddTickerQ(options =>
            {
                options.AddOperationalStore(efOptions =>
                {
                    efOptions.UseTickerQDbContext<TickerQDbContext>(dbOptions =>
                    {
                        dbOptions.UseSqlite(
                            "Data Source=tickerq-console.db",
                            b => b.MigrationsAssembly("Framework.Ticker.Sample.Console")
                        );
                    });
                });
            });

            services.AddHostedService<SampleScheduler>();
        }
    )
    .Build();

// Ensure TickerQ operational store schema is applied
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TickerQDbContext>();
    await db.Database.MigrateAsync();
}

// Build function metadata so TickerFunctionProvider.TickerFunctions is initialized
TickerFunctionProvider.Build();

await host.RunAsync();

// Simple sample job
public class ConsoleSampleJobs
{
    [TickerFunction("ConsoleSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Console] Hello from TickerQ! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
public class SampleScheduler : IHostedService
{
    private readonly ITimeTickerManager<TimeTickerEntity> _timeTickerManager;

    public SampleScheduler(ITimeTickerManager<TimeTickerEntity> timeTickerManager)
    {
        _timeTickerManager = timeTickerManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _timeTickerManager.AddAsync(
            new TimeTickerEntity
            {
                Function = "ConsoleSample_HelloWorld",
                ExecutionTime = DateTime.UtcNow.AddSeconds(5),
            },
            cancellationToken
        );

        if (!result.IsSucceeded)
        {
            Console.WriteLine($"Failed to schedule console sample job. Exception: {result.Exception}");
            return;
        }

        Console.WriteLine(
            $"Scheduled console sample job with Id={result.Result.Id}, ScheduledFor={result.Result.ExecutionTime:O}"
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
