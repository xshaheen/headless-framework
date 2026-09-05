// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

public sealed class InMemoryCronOccurrenceLifecycleTests : TestBase
{
    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTimeOffset _Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task reseeding_with_a_new_registered_version_preserves_the_stored_payload_contract(
        bool changeExpression
    )
    {
        using var fixture = new Fixture();
        var provider = fixture.Provider;
        var seed = new CronSeedDefinition(
            "seeded.contract",
            "* * * * *",
            MissedRunPolicy.Skip,
            60,
            ContractVersion: "1"
        );
        await provider.MigrateDefinedCronJobsAsync([seed], AbortToken);
        var definitionId = (await provider.GetAllCronJobExpressionsAsync(AbortToken)).Single().Id;
        var definition = (await provider.GetCronJobByIdAsync(definitionId, AbortToken))!;
        definition.Request = [1, 2, 3];
        await provider.UpdateCronJobsAsync([definition], AbortToken);

        var upgradedSeed = seed with
        {
            ContractVersion = "2",
            Expression = changeExpression ? "*/2 * * * *" : seed.Expression,
        };
        await provider.MigrateDefinedCronJobsAsync(
            [upgradedSeed, upgradedSeed with { Function = "new.contract" }],
            AbortToken
        );

        var stored = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
        stored.Function.Should().Be(seed.Function);
        stored.ContractVersion.Should().Be("1");
        stored.Request.Should().Equal(1, 2, 3);
        stored.Expression.Should().Be(upgradedSeed.Expression);
        (await provider.GetAllCronJobExpressionsAsync(AbortToken))
            .Single(x => x.Function == "new.contract")
            .ContractVersion.Should()
            .Be("2");

        var occurrence = fixture.Occurrence(stored, JobStatus.Idle);
        await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        occurrence.Function.Should().Be(seed.Function);
        occurrence.ContractVersion.Should().Be("1");
        (await provider.GetCronJobOccurrenceRequestAsync(occurrence.Id, AbortToken)).Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("replacement-lineage")]
    public async Task ordinary_job_edits_preserve_captured_lineage(string? incomingLineage)
    {
        using var fixture = new Fixture();
        var provider = fixture.Provider;
        var timeJob = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "causal.time",
            CorrelationId = "root",
            CausationId = "parent",
            TenantId = "tenant",
        };
        await provider.AddTimeJobsAsync([timeJob], AbortToken);
        await provider.UpdateTimeJobsAsync(
            [
                new TimeJobEntity
                {
                    Id = timeJob.Id,
                    Function = timeJob.Function,
                    Description = "Edited in dashboard",
                    Retries = 4,
                    CorrelationId = incomingLineage,
                    CausationId = incomingLineage,
                },
            ],
            AbortToken
        );
        var storedTime = (await provider.GetTimeJobByIdAsync(timeJob.Id, AbortToken))!;
        storedTime.Description.Should().Be("Edited in dashboard");
        storedTime.Retries.Should().Be(4);
        storedTime.CorrelationId.Should().Be("root");
        storedTime.CausationId.Should().Be("parent");
        storedTime.TenantId.Should().Be("tenant");

        var definition = fixture.CronJob(isPaused: false);
        definition.CorrelationId = "root";
        definition.CausationId = "parent";
        await provider.InsertCronJobsAsync([definition], AbortToken);
        foreach (var atomic in new[] { false, true })
        {
            var edit = new CronJobEntity
            {
                Id = definition.Id,
                Function = definition.Function,
                Expression = definition.Expression,
                Description = atomic ? "Atomic dashboard edit" : "Generic dashboard edit",
                Retries = atomic ? 5 : 4,
                CorrelationId = incomingLineage,
                CausationId = incomingLineage,
            };
            if (atomic)
            {
                var result = await provider.UpdateCronJobsAtomicallyAsync(
                    [new(edit, definition.ScheduleRevision, null)],
                    _Now,
                    AbortToken
                );
                result.Should().ContainSingle();
                result![0].CorrelationId.Should().Be("root");
                result[0].CausationId.Should().Be("parent");
            }
            else
            {
                (await provider.UpdateCronJobsAsync([edit], AbortToken)).Should().Be(1);
            }

            var storedCron = (await provider.GetCronJobByIdAsync(definition.Id, AbortToken))!;
            storedCron.Description.Should().Be(edit.Description);
            storedCron.Retries.Should().Be(edit.Retries);
            storedCron.CorrelationId.Should().Be("root");
            storedCron.CausationId.Should().Be("parent");
            var occurrence = fixture.Occurrence(storedCron, JobStatus.Idle);
            occurrence.ExecutionTime = _Now.AddMinutes(atomic ? 2 : 1).UtcDateTime;
            await provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
            occurrence.CorrelationId.Should().Be("root");
            occurrence.CausationId.Should().Be("parent");
        }
    }

    [Fact]
    public async Task materialization_owns_bytes_and_ignores_a_stale_caller_tuple()
    {
        using var fixture = new Fixture();
        var definition = fixture.CronJob(isPaused: false);
        definition.Request = [1, 2, 3];
        await fixture.Provider.InsertCronJobsAsync([definition], AbortToken);
        var occurrence = fixture.Occurrence(definition, JobStatus.Idle);
        occurrence.Function = "stale";
        occurrence.ContractVersion = "old";
        occurrence.Request = [9];
        await fixture.Provider.InsertCronJobOccurrencesAsync([occurrence], AbortToken);
        occurrence.Function.Should().Be(definition.Function);
        occurrence.ContractVersion.Should().Be("1");
        occurrence.Request.Should().Equal(1, 2, 3);

        definition.Request[0] = 8;
        occurrence.Request![1] = 8;
        var loaded = (
            await fixture.Provider.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, AbortToken)
        ).Single();
        loaded.Request![2] = 8;
        var request = await fixture.Provider.GetCronJobOccurrenceRequestAsync(occurrence.Id, AbortToken);
        request.Should().Equal(1, 2, 3);
        request[0] = 9;
        (await fixture.Provider.GetCronJobOccurrenceRequestAsync(occurrence.Id, AbortToken)).Should().Equal(1, 2, 3);

        definition.ContractVersion = "2";
        await fixture.Provider.UpdateCronJobsAsync([definition], AbortToken);
        var pending = (
            await fixture.Provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], AbortToken)
        ).Single();
        var registry = JobFunctionRegistryBuilder.Build(
            [],
            [],
            [
                new KeyValuePair<string, JobFunctionDescriptor>(
                    definition.Function,
                    new(definition.Function, null, "", JobPriority.Normal, 0, "2")
                ),
            ]
        );
        var execution = new JobExecutionState
        {
            FunctionName = pending.Function,
            ContractVersion = pending.ContractVersion,
        };
        JobsExecutionContext.CacheFunctionReferences(execution, registry);
        execution.CachedDelegate.Should().BeNull();
        execution
            .ContractVersionError.Should()
            .Contain("version '1'")
            .And.Contain("registers '2'")
            .And.Contain("not deserialized");
    }

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
