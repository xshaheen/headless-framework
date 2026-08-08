// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

public sealed class InMemoryCronOccurrenceLifecycleTests : TestBase
{
    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTimeOffset _Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task should_claim_only_eligible_occurrences_and_honor_cancellation_when_acquiring_immediately()
    {
        using var fixture = new Fixture();
        var active = fixture.CronJob(isPaused: false);
        var paused = fixture.CronJob(isPaused: true);
        await fixture.Provider.InsertCronJobsAsync([active, paused], AbortToken);

        var eligible = fixture.Occurrence(active, JobStatus.Idle);
        var pausedOccurrence = fixture.Occurrence(paused, JobStatus.Idle);
        var liveForeignClaim = fixture.Occurrence(
            active,
            JobStatus.Queued,
            ownerId: _NodeB,
            lockedUntil: _Now.AddMinutes(1).UtcDateTime
        );
        var terminal = fixture.Occurrence(active, JobStatus.Succeeded);
        await fixture.Provider.InsertCronJobOccurrencesAsync(
            [eligible, pausedOccurrence, liveForeignClaim, terminal],
            AbortToken
        );

        var acquired = await fixture.Provider.AcquireImmediateCronOccurrencesAsync(
            [eligible.Id, pausedOccurrence.Id, liveForeignClaim.Id, terminal.Id, Guid.NewGuid()],
            AbortToken
        );

        var claimed = acquired.Should().ContainSingle().Subject;
        claimed.Id.Should().Be(eligible.Id);
        claimed.Status.Should().Be(JobStatus.InProgress);
        claimed.OwnerId.Should().Be(_NodeA);
        claimed.LockedUntil.Should().Be(_Now.Add(_Lease).UtcDateTime);
        claimed.CronJob.Should().NotBeNull().And.Match<CronJobEntity>(job => job.Id == active.Id);

        (await fixture.Provider.AcquireImmediateCronOccurrencesAsync([eligible.Id], AbortToken)).Should().BeEmpty();

        var cancelled = async () =>
            await fixture.Provider.AcquireImmediateCronOccurrencesAsync(
                [pausedOccurrence.Id],
                new CancellationToken(canceled: true)
            );
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_apply_each_death_policy_when_an_in_progress_lease_has_lapsed()
    {
        using var fixture = new Fixture();
        var cron = fixture.CronJob(isPaused: false);
        await fixture.Provider.InsertCronJobsAsync([cron], AbortToken);

        var queued = fixture.Occurrence(cron, JobStatus.Queued, ownerId: _NodeA, retryCount: 2);
        var retry = fixture.Occurrence(
            cron,
            JobStatus.InProgress,
            policy: NodeDeathPolicy.Retry,
            ownerId: _NodeA,
            lockedUntil: _Now.AddSeconds(-1).UtcDateTime,
            retryCount: 2
        );
        var markFailed = fixture.Occurrence(
            cron,
            JobStatus.InProgress,
            policy: NodeDeathPolicy.MarkFailed,
            ownerId: _NodeA,
            lockedUntil: _Now.AddSeconds(-1).UtcDateTime
        );
        var skip = fixture.Occurrence(
            cron,
            JobStatus.InProgress,
            policy: NodeDeathPolicy.Skip,
            ownerId: _NodeA,
            lockedUntil: _Now.AddSeconds(-1).UtcDateTime
        );
        var healthy = fixture.Occurrence(
            cron,
            JobStatus.InProgress,
            policy: NodeDeathPolicy.Retry,
            ownerId: _NodeA,
            lockedUntil: _Now.AddMinutes(1).UtcDateTime
        );
        var otherOwner = fixture.Occurrence(cron, JobStatus.Idle, ownerId: _NodeB);
        await fixture.Provider.InsertCronJobOccurrencesAsync(
            [queued, retry, markFailed, skip, healthy, otherOwner],
            AbortToken
        );

        var affected = await fixture.Provider.ReleaseDeadNodeOccurrenceResourcesAsync(_NodeA, AbortToken);

        affected.Should().Be(4);
        var stored = (await fixture.Provider.GetAllCronJobOccurrencesAsync(predicate: null, AbortToken)).ToDictionary(
            occurrence => occurrence.Id
        );

        stored[queued.Id]
            .Should()
            .Match<CronJobOccurrenceEntity<CronJobEntity>>(occurrence =>
                occurrence.Status == JobStatus.Idle
                && occurrence.OwnerId == null
                && occurrence.LockedUntil == null
                && occurrence.RetryCount == 2
            );
        stored[retry.Id]
            .Should()
            .Match<CronJobOccurrenceEntity<CronJobEntity>>(occurrence =>
                occurrence.Status == JobStatus.Idle
                && occurrence.OwnerId == null
                && occurrence.LockedUntil == null
                && occurrence.RetryCount == 3
            );
        stored[markFailed.Id]
            .Should()
            .Match<CronJobOccurrenceEntity<CronJobEntity>>(occurrence =>
                occurrence.Status == JobStatus.Failed
                && occurrence.LockedUntil == null
                && occurrence.ExceptionMessage == "Node is not alive!"
                && occurrence.ExecutedAt == _Now
            );
        stored[skip.Id]
            .Should()
            .Match<CronJobOccurrenceEntity<CronJobEntity>>(occurrence =>
                occurrence.Status == JobStatus.Skipped
                && occurrence.LockedUntil == null
                && occurrence.SkippedReason == "Node is not alive!"
                && occurrence.ExecutedAt == _Now
            );
        stored[healthy.Id].Status.Should().Be(JobStatus.InProgress);
        stored[healthy.Id].OwnerId.Should().Be(_NodeA);
        stored[otherOwner.Id].OwnerId.Should().Be(_NodeB);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;

        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
            services.AddHeadlessGuidGenerator();
            services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = _Lease });
            _services = services.BuildServiceProvider();
            Provider = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(_services);
        }

        public JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> Provider { get; }

        public CronJobEntity CronJob(bool isPaused) =>
            new()
            {
                Id = Guid.NewGuid(),
                Function = "cron-job",
                Expression = "* * * * *",
                IsPaused = isPaused,
            };

        public CronJobOccurrenceEntity<CronJobEntity> Occurrence(
            CronJobEntity cron,
            JobStatus status,
            NodeDeathPolicy policy = NodeDeathPolicy.Retry,
            string? ownerId = null,
            DateTime? lockedUntil = null,
            int retryCount = 0
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                CronJobId = cron.Id,
                CronJob = cron,
                ExecutionTime = _Now.UtcDateTime,
                Status = status,
                OnNodeDeath = policy,
                OwnerId = ownerId,
                LockedUntil = lockedUntil,
                RetryCount = retryCount,
            };

        public void Dispose() => _services.Dispose();
    }
}
