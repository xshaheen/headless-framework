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
        System.Console.WriteLine($"[Console] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
public class SampleScheduler(ITimeJobManager<TimeJobEntity> timeJobManager) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await timeJobManager.AddAsync(
            new TimeJobEntity { Function = "ConsoleSample_HelloWorld", ExecutionTime = DateTime.UtcNow.AddSeconds(5) },
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
