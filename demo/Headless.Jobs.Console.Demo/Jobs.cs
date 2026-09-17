using Headless.Jobs.Base;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Jobs.Console.Demo;

// Simple sample job
public static class ConsoleSampleJobs
{
    internal const string FunctionName = "ConsoleSample_HelloWorld";

    [JobFunction(FunctionName)]
    public static Task HelloWorldAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        System.Console.WriteLine($"[Console] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}

// Hosted service that schedules a single job on startup. IJobScheduler is scoped (it reads the scope's
// IUnitOfWorkManager.Current), so a hosted service — which lives for the app's lifetime, not one operation —
// creates its own scope per run rather than injecting the scoped service into its own (effectively singleton)
// constructor.
public class SampleScheduler(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();

        Guid jobId;
        try
        {
            var descriptor = AppJobs.ConsoleSample_u005F_HelloWorld;
            jobId = await scheduler.EnqueueAsync(
                descriptor,
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
