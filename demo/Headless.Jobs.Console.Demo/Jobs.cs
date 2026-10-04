using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.Console.Demo;

// Simple sample job
[Job("console-demo.hello-world")]
public sealed class HelloWorldJob : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        System.Console.WriteLine($"[Console] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return ValueTask.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup. The injected IJobScheduler is the autonomous
// receiver — a singleton that writes outside any unit of work — which is exactly what a startup schedule wants.
public class SampleScheduler(IJobScheduler scheduler) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Guid jobId;
        try
        {
            jobId = await scheduler.EnqueueAsync<HelloWorldJob>(
                new JobOptions { Description = "Sample console demo job" },
                cancellationToken
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            System.Console.WriteLine($"Failed to schedule console sample job. Exception: {e}");
            return;
        }

        System.Console.WriteLine($"Scheduled console sample job with Id={jobId}");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
