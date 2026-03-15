using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Console.Demo;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.DependencyInjection;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(
        (_, services) =>
        {
            // Configure Jobs with SQLite operational store (file-based)
            services.AddJobs(options =>
            {
                options.AddOperationalStore(efOptions =>
                {
                    efOptions.UseJobsDbContext<JobsDbContext>(dbOptions =>
                    {
                        dbOptions.UseSqlite(
                            "Data Source=tickerq-console.db",
                            b => b.MigrationsAssembly("Headless.Jobs.Console.Demo")
                        );
                    });
                });
            });

            services.AddHostedService<SampleScheduler>();
        }
    )
    .Build();

// Ensure Jobs operational store schema is applied
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
    await db.Database.MigrateAsync();
}

// Build function metadata so TickerFunctionProvider.TickerFunctions is initialized
TickerFunctionProvider.Build();

await host.RunAsync();

namespace Headless.Jobs.Console.Demo
{
    // Simple sample job
    public static class ConsoleSampleJobs
    {
        [TickerFunction("ConsoleSample_HelloWorld")]
        public static Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
        {
            System.Console.WriteLine(
                $"[Console] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}"
            );
            return Task.CompletedTask;
        }
    }

    // Hosted service that schedules a single job on startup
    public class SampleScheduler(ITimeTickerManager<TimeTickerEntity> timeTickerManager) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var result = await timeTickerManager.AddAsync(
                new TimeTickerEntity
                {
                    Function = "ConsoleSample_HelloWorld",
                    ExecutionTime = DateTime.UtcNow.AddSeconds(5),
                },
                cancellationToken
            );

            if (!result.IsSucceeded)
            {
                System.Console.WriteLine($"Failed to schedule console sample job. Exception: {result.Exception}");
                return;
            }

            System.Console.WriteLine(
                $"Scheduled console sample job with Id={result.Result.Id}, ScheduledFor={result.Result.ExecutionTime:O}"
            );
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
