using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.Console.Demo;

// Simple sample job
public static class ConsoleSampleJobs
{
    [JobFunction("ConsoleSample_HelloWorld")]
    public static Task HelloWorldAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        System.Console.WriteLine($"[Console] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
public class SampleScheduler(ITimeJobManager<TimeJobEntity> timeJobManager) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        TimeJobEntity job;
        try
        {
            job = await timeJobManager.AddAsync(
                new TimeJobEntity
                {
                    Function = "ConsoleSample_HelloWorld",
                    Description = "Sample console demo job",
                    ExecutionTime = DateTime.UtcNow.AddSeconds(5),
                },
                cancellationToken
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            System.Console.WriteLine($"Failed to schedule console sample job. Exception: {e}");
            return;
        }

        System.Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Scheduled console sample job with Id={job.Id}, ScheduledFor={job.ExecutionTime:O}"
            )
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
