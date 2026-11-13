// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A retry whose delay is a second or less runs in process, under the lease of the attempt that failed. Its write of
/// the spent retry count must land on the row that attempt still holds, so the run retries instead of reading as a lost
/// lease and staying InProgress until the stalled-run sweep.
/// </summary>
public abstract class JobsInProcessRetryConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly TimeSpan _WaitBudget = TimeSpan.FromSeconds(60);

    public virtual async Task in_process_retries_spend_the_budget_and_end_the_run_failed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var id = Guid.NewGuid();

        using var node = fixture.BuildHost(
            "node-a",
            configureJobs: jobs => jobs.AddModule<AlwaysFailingJobsModule>(),
            runBackgroundServices: true
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(node, ct);
        await node.StartAsync(ct);

        try
        {
            // given: a job that throws on every attempt, with two retries that wait no time
            var store = node.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await store.AddTimeJobsAsync(
                [
                    new TimeJobEntity
                    {
                        Id = id,
                        Function = AlwaysFailingJobsModule.FunctionName,
                        ExecutionTime = DateTime.UtcNow,
                        Retries = 2,
                        RetryIntervals = [0],
                        OnNodeDeath = NodeDeathPolicy.Retry,
                    },
                ],
                ct
            );

            // when: the scheduler runs it to the end of its budget
            var stored = await _WaitForTerminalAsync(store, id, ct);

            // then: three attempts ran and the run settled Failed with its whole budget spent
            stored.Status.Should().Be(JobStatus.Failed);
            stored.RetryCount.Should().Be(2);
            AlwaysFailingJobsModule.Invocations(id).Should().Be(3);
        }
        finally
        {
            await node.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<TimeJobEntity> _WaitForTerminalAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        CancellationToken ct
    )
    {
        var started = TimeProvider.System.GetTimestamp();
        TimeJobEntity? stored = null;

        while (TimeProvider.System.GetElapsedTime(started) < _WaitBudget)
        {
            stored = await store.GetTimeJobByIdAsync(id, ct);

            if (
                stored is
                { Status: JobStatus.Failed or JobStatus.Succeeded or JobStatus.Cancelled or JobStatus.Skipped }
            )
            {
                return stored;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }

        throw new TimeoutException(
            $"The run did not settle within {_WaitBudget}: status {stored?.Status}, retry count {stored?.RetryCount}, "
                + $"invocations {AlwaysFailingJobsModule.Invocations(id)}."
        );
    }
}

/// <summary>One time-job function that throws on every invocation. State is keyed by job id.</summary>
public sealed class AlwaysFailingJobsModule : IJobsModule
{
    public const string FunctionName = "AlwaysFailing_Sample";

    private static readonly ConcurrentDictionary<Guid, int> _Invocations = new();

    private AlwaysFailingJobsModule() { }

    public static int Invocations(Guid jobId) => _Invocations.GetValueOrDefault(jobId);

    private static Task _RunAsync(Guid jobId)
    {
        _Invocations.AddOrUpdate(jobId, 1, static (_, count) => count + 1);

        throw new InvalidOperationException("The sample job always fails.");
    }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [FunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.Normal,
                    Delegate = static (_, context, _) => _RunAsync(context.Id),
                    MaxConcurrency = 0,
                    JobType = typeof(AlwaysFailingJob),
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [FunctionName] = new(FunctionName, null, string.Empty, JobPriority.Normal, 0),
            }
        );
    }

    public sealed class AlwaysFailingJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
