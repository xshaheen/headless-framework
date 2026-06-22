using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;

namespace Headless.Jobs.Dashboard.Jwt.Demo;

/// <summary>
/// Seeds cron + time jobs on startup, then schedules more every 30 seconds.
/// </summary>
public sealed class DemoJobSeeder(IServiceScopeFactory scopeFactory, ILogger<DemoJobSeeder> logger) : BackgroundService
{
    private static readonly string[] _TimeJobFunctions =
    [
        "Demo_OrderProcessing",
        "Demo_DataSync",
        "Demo_ReportGeneration",
        "Demo_PaymentReconciliation",
    ];

    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for jobs framework bootstrap
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        // Seed cron jobs
        logger.LogInformation("Seeding cron jobs...");
        await _SeedCronJobs(stoppingToken);

        // Initial burst — populate dashboard with some history
        logger.LogInformation("Seeding initial time jobs...");
        await _ScheduleTimeJobs(15, stoppingToken);
        logger.LogInformation("Initial seed complete — switching to 30s interval");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            try
            {
                var count = Random.Shared.Next(2, 5);
                await _ScheduleTimeJobs(count, stoppingToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Scheduled {Count} time jobs (cycle #{Cycle})", count, _counter);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Error scheduling demo jobs");
            }
        }
    }

    private async Task _SeedCronJobs(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var cronManager = scope.ServiceProvider.GetRequiredService<ICronJobManager<CronJobEntity>>();

        try
        {
            await cronManager.AddAsync(
                new CronJobEntity
                {
                    Function = "Demo_CleanupExpiredSessions",
                    Description = "Purge expired user sessions every 2 minutes",
                    Expression = "0 */2 * * * *",
                },
                ct
            );
            logger.LogInformation("Seeded cron job: Demo_CleanupExpiredSessions");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError("Failed to seed CleanupExpiredSessions: {Error}", e.Message);
        }

        try
        {
            await cronManager.AddAsync(
                new CronJobEntity
                {
                    Function = "Demo_HealthCheck",
                    Description = "Run infrastructure health check every minute",
                    Expression = "0 * * * * *",
                },
                ct
            );
            logger.LogInformation("Seeded cron job: Demo_HealthCheck");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError("Failed to seed HealthCheck: {Error}", e.Message);
        }
    }

    private async Task _ScheduleTimeJobs(int count, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var timeManager = scope.ServiceProvider.GetRequiredService<ITimeJobManager<TimeJobEntity>>();

        for (var i = 0; i < count; i++)
        {
            _counter++;
            var function = _TimeJobFunctions[Random.Shared.Next(_TimeJobFunctions.Length)];

            // Mix of immediate (past) and near-future execution times
            var delay =
                Random.Shared.Next(10) < 4
                    ? TimeSpan.FromSeconds(-1) // immediate
                    : TimeSpan.FromSeconds(Random.Shared.Next(5, 30));

            await timeManager.AddAsync(
                new TimeJobEntity
                {
                    Function = function,
                    Description = $"{function.Replace("Demo_", "", StringComparison.Ordinal)} job #{_counter}",
                    ExecutionTime = DateTime.UtcNow.Add(delay),
                },
                ct
            );
        }
    }
}
