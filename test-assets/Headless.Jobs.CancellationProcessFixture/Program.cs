// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs.Base;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.CancellationProcessFixture;

internal static class Program
{
    private const string _ConnectionEnvironmentVariable = "HEADLESS_JOBS_CANCELLATION_SMOKE_CONNECTION";
    internal const string FunctionName = "CancellationProcessSmoke";

    public static async Task<int> Main(string[] args)
    {
        if (
            args.Length != 1
            || !Guid.TryParse(args[0], out var jobId)
            || Environment.GetEnvironmentVariable(_ConnectionEnvironmentVariable)
                is not { Length: > 0 } connectionString
        )
        {
            return 64;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddHeadlessCoordination(setup =>
        {
            setup.UsePostgreSql(connectionString);
            setup.Configure(options =>
            {
                options.ClusterName = "jobs-it";
                options.ConfiguredNodeId = "cancellation-process-worker";
                options.HeartbeatInterval = TimeSpan.FromMilliseconds(200);
                options.SuspicionThreshold = TimeSpan.FromMilliseconds(600);
                options.DeadThreshold = TimeSpan.FromMilliseconds(1200);
                options.DeadRetentionWindow = TimeSpan.FromMilliseconds(1200);
            });
        });
        builder.Services.AddHeadlessJobs(options =>
        {
            options.ConfigureScheduler(scheduler =>
            {
                scheduler.LeaseDuration = TimeSpan.FromSeconds(3);
                scheduler.LeaseRenewalInterval = TimeSpan.FromMilliseconds(500);
                scheduler.CancellationObservationInterval = TimeSpan.FromMilliseconds(100);
                scheduler.FallbackIntervalChecker = TimeSpan.FromMilliseconds(100);
            });
            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<JobsDbContext>(db => db.UseNpgsql(connectionString), "jobs");
                ef.UsePostgreSqlClaims();
            });
        });

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await host.StartAsync(timeout.Token).ConfigureAwait(false);
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await Console.Out.WriteLineAsync("HOST_READY").ConfigureAwait(false);
            await Console.Out.FlushAsync(timeout.Token).ConfigureAwait(false);

            while (!timeout.IsCancellationRequested)
            {
                if (
                    await persistence.GetTimeJobByIdAsync(jobId, timeout.Token).ConfigureAwait(false) is
                    { Status: JobStatus.Cancelled }
                )
                {
                    await Console.Out.WriteLineAsync("OBSERVED").ConfigureAwait(false);
                    await Console.Out.FlushAsync(timeout.Token).ConfigureAwait(false);
                    return 0;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        finally
        {
            await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return 2;
    }
}

internal static class CancellationProcessJobs
{
    [JobFunction(Program.FunctionName)]
    public static async Task WaitForCancellationAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        _ = context;
        await Console.Out.WriteLineAsync("USER_CODE").ConfigureAwait(false);
        await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }
}
