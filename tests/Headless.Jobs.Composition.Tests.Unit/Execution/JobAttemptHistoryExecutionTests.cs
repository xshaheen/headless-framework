// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Managers;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Execution;

/// <summary>
/// Attempt history through the real executor, retry pipeline, manager, and in-memory store: each handler invocation is
/// one attempt, and each in-process retry closes the attempt that failed with its exception and fingerprint.
/// </summary>
public sealed class JobAttemptHistoryExecutionTests : TestBase
{
    private const string _Function = "attempts.job";

    [Fact]
    public async Task should_store_three_attempts_in_order_when_the_handler_fails_twice_then_succeeds()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
        var registrations = new ServiceCollection();
        registrations.AddSingleton<TimeProvider>(time);
        registrations.AddHeadlessGuidGenerator();
        await using var services = registrations.BuildServiceProvider();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var options = new SchedulerOptionsBuilder();
        var manager = new InternalJobsManager<TimeJobEntity, CronJobEntity>(
            store,
            time,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<TimeJobEntity, CronJobEntity>>.Instance,
            JobsRequestSerializationOptions.Default,
            services.GetRequiredService<IGuidGenerator>(),
            services,
            options
        );
        var job = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            ExecutionTime = time.GetUtcNow().UtcDateTime,
            Retries = 2,
            RetryIntervals = [0],
        };
        (await store.AddTimeJobsAsync([job], AbortToken)).Should().Be(1);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([job.Id], AbortToken)).Single();
        var context = new JobExecutionState
        {
            JobId = claimed.Id,
            FunctionName = claimed.Function,
            ContractVersion = claimed.ContractVersion,
            Type = JobType.TimeJob,
            ExecutionTime = claimed.ExecutionTime!.Value,
            Status = claimed.Status,
            RetryCount = claimed.RetryCount,
            Retries = claimed.Retries,
            RetryIntervals = claimed.RetryIntervals,
            ClaimOwner = claimed.Lease.Holder,
            ClaimGeneration = claimed.Lease.Generation,
        };
        var registry = JobFunctionRegistryBuilder.Build(
            [
                new KeyValuePair<string, JobFunctionRegistration>(
                    _Function,
                    new()
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        MaxConcurrency = 0,
                        Delegate = (_, execution, _) =>
                            execution.RetryCount < 2
                                ? throw new InvalidOperationException($"transient {execution.RetryCount}")
                                : Task.CompletedTask,
                    }
                ),
            ],
            [],
            [
                new KeyValuePair<string, JobFunctionDescriptor>(
                    _Function,
                    new(_Function, null, "", JobPriority.Normal, 0, claimed.ContractVersion)
                ),
            ]
        );
        JobsExecutionContext.CacheFunctionReferences(context, registry);
        var handler = new JobsExecutionTaskHandler(
            services,
            time,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            options,
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(registry)
        );

        await handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken);

        (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!.Status.Should().Be(JobStatus.Succeeded);
        var attempts = await store.GetTimeJobAttemptsAsync(job.Id, AbortToken);
        attempts.Select(x => x.Number).Should().Equal(1, 2, 3);
        attempts
            .Select(x => x.Outcome)
            .Should()
            .Equal(JobAttemptOutcome.Failed, JobAttemptOutcome.Failed, JobAttemptOutcome.Succeeded);
        attempts
            .Should()
            .AllSatisfy(attempt =>
            {
                attempt.OwnerId.Should().Be(claimed.Lease.Holder);
                attempt.StartedAt.Should().NotBeNull();
                attempt.EndedAt.Should().NotBeNull();
            });
        attempts[0].ExceptionMessage.Should().Contain("transient 0");
        attempts[1].ExceptionMessage.Should().Contain("transient 1");
        attempts[0].FailureFingerprint.Should().StartWith(JobFailureFingerprints.VersionPrefix);
        attempts[1].FailureFingerprint.Should().Be(attempts[0].FailureFingerprint, "both attempts failed the same way");
        attempts[2].ExceptionMessage.Should().BeNull();
        attempts[2].FailureFingerprint.Should().BeNull();
    }
}
