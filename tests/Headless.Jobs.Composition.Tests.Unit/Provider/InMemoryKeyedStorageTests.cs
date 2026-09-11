// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

public sealed class InMemoryKeyedStorageTests : TestBase
{
    [Fact]
    public async Task keyed_lookup_does_not_visit_retained_history()
    {
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<CountingTimeJob, CronJobEntity>(services);
        var key = new JobKey("retained");
        JobScheduleResult result = null!;
        for (var generation = 1; generation <= 256; generation++)
        {
            result = await store.ScheduleKeyedTimeJobAsync(
                key,
                _CountingCandidate(),
                generation == 1 ? null : generation - 1,
                AbortToken
            );
            result.Generation.Should().Be(generation);
        }

        CountingTimeJob.GenerationReads = 0;
        var observed = await store.ScheduleKeyedTimeJobAsync(key, _CountingCandidate(), cancellationToken: AbortToken);
        var cancelled = await store.CancelKeyedTimeJobAsync(new JobKeyScope("deadline"), key, 256, AbortToken);

        observed.Disposition.Should().Be(JobScheduleDisposition.Existing);
        observed.RunId.Should().Be(result.RunId);
        cancelled.Disposition.Should().Be(JobScheduleDisposition.Cancelled);
        cancelled.RunId.Should().Be(result.RunId);
        // Count metadata reads instead of timing a scan, so the regression check is independent of machine load.
        CountingTimeJob.GenerationReads.Should().BeLessThan(10);
    }

    [Fact]
    public async Task current_identity_uses_ordinal_tuple_components()
    {
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        (string? Tenant, string Function, string Key)[] identities =
        [
            (null, "deadline", "key"),
            ("Tenant", "deadline", "key"),
            ("tenant", "deadline", "key"),
            (null, "Deadline", "key"),
            (null, "deadline", "Key"),
            ("a:b", "c", "d"),
            ("a", "b:c", "d"),
            ("a", "b", "c:d"),
        ];

        var ids = new HashSet<Guid>();
        foreach (var (tenant, function, value) in identities)
        {
            var job = JobsKeyedSchedulingScenarios.Candidate();
            job.TenantId = tenant;
            job.Function = function;
            var key = new JobKey(value);
            var created = await store.ScheduleKeyedTimeJobAsync(key, job, cancellationToken: AbortToken);
            created.Disposition.Should().Be(JobScheduleDisposition.Created);
            ids.Add(created.RunId!.Value).Should().BeTrue();
            var cancelled = await store.CancelKeyedTimeJobAsync(new JobKeyScope(function, tenant), key, 1, AbortToken);
            cancelled.Disposition.Should().Be(JobScheduleDisposition.Cancelled);
            cancelled.RunId.Should().Be(created.RunId);
        }
    }

    [Fact]
    public async Task ordinary_mutations_and_rejected_keyed_deletion_preserve_current_identity()
    {
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var key = new JobKey("retained");
        var first = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate(),
            cancellationToken: AbortToken
        );
        var replacement = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate([4]),
            1,
            AbortToken
        );

        var ordinary = JobsKeyedSchedulingScenarios.Candidate();
        (await store.AddTimeJobsAsync([ordinary], AbortToken)).Should().Be(1);
        ordinary.Function = "changed";
        (await store.UpdateTimeJobsAsync([ordinary], AbortToken)).Should().Be(1);
        (await store.RemoveTimeJobsAsync([ordinary.Id], AbortToken)).Should().Be(1);
        (await store.AddTimeJobsAsync([ordinary], AbortToken)).Should().Be(1);
        foreach (var id in new[] { first.RunId!.Value, replacement.RunId!.Value })
        {
            var remove = () => store.RemoveTimeJobsAsync([id], AbortToken);
            await remove.Should().ThrowAsync<InvalidOperationException>();
        }

        (await store.RequestTimeJobCancellationAsync(replacement.RunId.Value, AbortToken)).Should().BeTrue();
        var observed = await store.ScheduleKeyedTimeJobAsync(
            key,
            JobsKeyedSchedulingScenarios.Candidate([4]),
            cancellationToken: AbortToken
        );
        observed.RunId.Should().Be(replacement.RunId);
        observed.Generation.Should().Be(2);
        observed.State.Should().Be(JobStatus.Cancelled);
        (await store.GetTimeJobByIdAsync(first.RunId.Value, AbortToken))!.IsCurrentGeneration.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task queue_start_and_lease_updates_do_not_wait_for_the_keyed_lock(bool keyed)
    {
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var job = JobsKeyedSchedulingScenarios.Candidate();
        if (keyed)
        {
            await store.ScheduleKeyedTimeJobAsync(new JobKey("lease"), job, cancellationToken: AbortToken);
        }
        else
        {
            await store.AddTimeJobsAsync([job], AbortToken);
        }
        job = (await store.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        var gate = (Lock)
            typeof(JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>)
                .GetField("_keyedOperations", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(store)!;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        // A blocking lock holder must not depend on thread-pool availability on small CI runners.
        var holder = Task.Factory.StartNew(
            () =>
            {
                lock (gate)
                {
                    held.SetResult();
                    release.Wait(TimeSpan.FromSeconds(15), AbortToken).Should().BeTrue();
                }
            },
            AbortToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );

        Task updates = Task.CompletedTask;
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
            updates = Task.Run(
                async () =>
                {
                    (await store.QueueTimeJobsAsync([job], AbortToken).ToArrayAsync(AbortToken))
                        .Should()
                        .ContainSingle();
                    var state = new JobExecutionState
                    {
                        JobId = job.Id,
                        FunctionName = job.Function,
                        Status = JobStatus.InProgress,
                    };
                    state.SetProperty(x => x.Status, JobStatus.InProgress);
                    (await store.UpdateTimeJobsWithUnifiedContextAsync([job.Id], state, AbortToken))
                        .Should()
                        .ContainSingle();
                    (await store.RenewTimeJobLeaseAsync(job.Id, AbortToken)).Should().Be(1);
                },
                AbortToken
            );
            await updates.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
        finally
        {
            release.Set();
            await holder;
            await updates.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task nonpositive_generations_are_rejected(long generation)
    {
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var key = new JobKey("guards");
        var schedule = () =>
            store.ScheduleKeyedTimeJobAsync(key, JobsKeyedSchedulingScenarios.Candidate(), generation, AbortToken);
        var cancel = () => store.CancelKeyedTimeJobAsync(new JobKeyScope("deadline"), key, generation, AbortToken);
        (await schedule.Should().ThrowAsync<ArgumentOutOfRangeException>())
            .Which.ParamName.Should()
            .Be("expectedGeneration");
        (await cancel.Should().ThrowAsync<ArgumentOutOfRangeException>())
            .Which.ParamName.Should()
            .Be("expectedGeneration");
    }

    [Fact]
    public async Task null_key_job_and_scope_are_rejected()
    {
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var key = new JobKey("guards");
        var nullJob = () => store.ScheduleKeyedTimeJobAsync(key, null!, cancellationToken: AbortToken);
        var nullScheduleKey = () =>
            store.ScheduleKeyedTimeJobAsync(
                null!,
                JobsKeyedSchedulingScenarios.Candidate(),
                cancellationToken: AbortToken
            );
        var nullScope = () => store.CancelKeyedTimeJobAsync(null!, key, 1, AbortToken);
        var nullCancelKey = () => store.CancelKeyedTimeJobAsync(new JobKeyScope("deadline"), null!, 1, AbortToken);
        (await nullJob.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("job");
        (await nullScheduleKey.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("key");
        (await nullScope.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("scope");
        (await nullCancelKey.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("key");
    }

    private static ServiceProvider _Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddHeadlessGuidGenerator();
        return services.BuildServiceProvider();
    }

    private static CountingTimeJob _CountingCandidate()
    {
        var candidate = JobsKeyedSchedulingScenarios.Candidate();
        return new CountingTimeJob
        {
            Id = candidate.Id,
            Function = candidate.Function,
            ContractVersion = candidate.ContractVersion,
            ExecutionTime = candidate.ExecutionTime,
            Request = candidate.Request,
        };
    }

    private sealed class CountingTimeJob : TimeJobEntity<CountingTimeJob>
    {
        public static int GenerationReads { get; set; }

        public override bool? IsCurrentGeneration
        {
            get
            {
                GenerationReads++;
                return base.IsCurrentGeneration;
            }
            internal set => base.IsCurrentGeneration = value;
        }
    }
}
