using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.Console.Demo;

// Simple sample job
public static class ConsoleSampleJobs
{
    internal const string FunctionName = "ConsoleSample_HelloWorld";

    [JobFunction(FunctionName)]
    public static Task HelloWorldAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        System.Console.WriteLine($"[Console] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup
public class SampleScheduler(IJobScheduler scheduler) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Guid jobId;
        try
        {
            var descriptor = JobFunctionProvider.JobFunctionDescriptors[ConsoleSampleJobs.FunctionName];
            jobId = await scheduler.EnqueueAsync(
                descriptor,
                new EnqueueOptions { Description = "Sample console demo job" },
                cancellationToken
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            System.Console.WriteLine($"Failed to schedule console sample job. Exception: {e}");
            return;
        }

        System.Console.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"Scheduled console sample job with Id={jobId}")
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
