// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Provider-neutral native claim behavior exercised exclusively through production registration.</summary>
public abstract class JobsClaimConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task cron_materialization_ignores_stale_caller_tuple_and_restart_claims_its_snapshot()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var definition = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "snapshot.v1",
            ContractVersion = "1",
            Expression = "* * * * *",
            Request = [1, 2, 3],
            CorrelationId = "business-root",
            CausationId = "business-cause",
        };
        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = definition.Id,
            Function = "stale.caller",
            ContractVersion = "obsolete",
            Request = [9],
            ExecutionTime = DateTime.UtcNow.AddMinutes(30),
        };
        using (var host = fixture.BuildHost("tuple-before-restart"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            await host.StartAsync(ct);
            try
            {
                var provider = host.Services.GetRequiredService<
                    IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
                >();
                await provider.InsertCronJobsAsync([definition], ct);
                await provider.InsertCronJobOccurrencesAsync([occurrence], ct);
                occurrence.Function.Should().Be("snapshot.v1");
                occurrence.ContractVersion.Should().Be("1");
                occurrence.Request.Should().Equal(1, 2, 3);

                definition.Function = "snapshot.v2";
                definition.ContractVersion = "2";
                definition.Request = [4, 5, 6];
                definition.CorrelationId = "later-root";
                await provider.UpdateCronJobsAsync([definition], ct);
            }
            finally
            {
                await host.StopAsync(ct);
            }
        }

        using var restarted = fixture.BuildHost("tuple-after-restart");
        await restarted.StartAsync(ct);
        try
        {
            var provider = restarted.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var dispatch = new JobManagerDispatchContext(definition.Id)
            {
                FunctionName = definition.Function,
                Expression = definition.Expression,
                ScheduleRevision = definition.ScheduleRevision,
                NextCronOccurrence = new NextCronOccurrence(occurrence.Id, occurrence.CreatedAt),
            };
            var queued = (
                await provider.QueueCronJobOccurrencesAsync((occurrence.ExecutionTime, [dispatch]), ct).ToArrayAsync(ct)
            )
                .Should()
                .ContainSingle()
                .Subject;
            queued.Function.Should().Be("snapshot.v1");
            queued.ContractVersion.Should().Be("1");
            queued.Request.Should().Equal(1, 2, 3);
            var acquired = (await provider.AcquireImmediateCronOccurrencesAsync([occurrence.Id], ct))
                .Should()
                .ContainSingle()
                .Subject;
            acquired.Function.Should().Be("snapshot.v1");
            acquired.ContractVersion.Should().Be("1");
            acquired.Request.Should().Equal(1, 2, 3);
            acquired.CorrelationId.Should().Be("business-root");
            acquired.CausationId.Should().Be("business-cause");
            acquired.TenantId.Should().BeNull();
            acquired.CronJob.Function.Should().Be("snapshot.v2");
            (await provider.GetCronJobOccurrenceRequestAsync(occurrence.Id, ct)).Should().Equal(1, 2, 3);

            // Simulate a started attempt lost with the prior host. The recovered row must keep its original
            // executable tuple while the independently persisted retry budget advances.
            await using (
                var context = await restarted
                    .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                    .CreateDbContextAsync(ct)
            )
            {
                await context
                    .Set<CronJobOccurrenceEntity<CronJobEntity>>()
                    .Where(x => x.Id == occurrence.Id)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(x => x.LockedUntil, DateTime.UtcNow.AddMinutes(-5))
                                .SetProperty(x => x.ExecutionTime, DateTime.UtcNow.AddMinutes(-10))
                                .SetProperty(x => x.RetryCount, 2),
                        ct
                    );
            }
            (await provider.ReclaimStalledCronJobOccurrencesAsync(ct)).Should().Be(1);
            var retry = (await provider.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct))
                .Should()
                .ContainSingle()
                .Subject;
            retry.Function.Should().Be("snapshot.v1");
            retry.ContractVersion.Should().Be("1");
            retry.Request.Should().Equal(1, 2, 3);
            retry.RetryCount.Should().Be(3);
            retry.CorrelationId.Should().Be("business-root");
            retry.CausationId.Should().Be("business-cause");
        }
        finally
        {
            await restarted.StopAsync(ct);
        }
    }

    public virtual async Task contract_columns_preserve_ordinal_case_and_reject_invalid_new_writes()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("contract-columns");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);
        try
        {
            var provider = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var lower = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "case.name",
                ContractVersion = "vOne",
            };
            var upper = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "Case.Name",
                ContractVersion = "VOne",
            };
            await provider.AddTimeJobsAsync([lower, upper], ct);
            (await provider.GetTimeJobsAsync(x => x.Function == "case.name", ct))
                .Should()
                .ContainSingle()
                .Which.Id.Should()
                .Be(lower.Id);
            (await provider.GetTimeJobsAsync(x => x.ContractVersion == "VOne", ct))
                .Should()
                .ContainSingle()
                .Which.Id.Should()
                .Be(upper.Id);
            await using var context = await host
                .Services.GetRequiredService<IDbContextFactory<JobsDbContext>>()
                .CreateDbContextAsync(ct);
            foreach (
                var type in new[]
                {
                    typeof(TimeJobEntity),
                    typeof(CronJobEntity),
                    typeof(CronJobOccurrenceEntity<CronJobEntity>),
                }
            )
            {
                var entity = context.Model.FindEntityType(type)!;
                entity.FindProperty("Function")!.GetMaxLength().Should().Be(Headless.Jobs.JobContract.NameMaxLength);
                entity
                    .FindProperty("ContractVersion")!
                    .GetMaxLength()
                    .Should()
                    .Be(Headless.Jobs.JobContract.VersionMaxLength);
            }
            foreach (
                var invalid in new[] { "", " ", "v1 ", new string('x', Headless.Jobs.JobContract.VersionMaxLength + 1) }
            )
            {
                var rejectedId = Guid.NewGuid();
                var write = () =>
                    provider.AddTimeJobsAsync(
                        [
                            new TimeJobEntity
                            {
                                Id = rejectedId,
                                Function = "invalid",
                                ContractVersion = invalid,
                            },
                        ],
                        ct
                    );
                var failure = await write.Should().ThrowAsync<DbUpdateException>();
                failure
                    .Which.InnerException.Should()
                    .BeOfType<ArgumentException>()
                    .Which.Message.Should()
                    .Contain("Job contract identities");
                (await provider.GetTimeJobByIdAsync(rejectedId, ct)).Should().BeNull();
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task materialized_cron_request_survives_parent_payload_edit_and_provider_restart()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var cronId = Guid.NewGuid();
        byte[] originalRequest = [123, 34, 118, 34, 58, 49, 125];
        byte[] replacementRequest = [123, 34, 118, 34, 58, 50, 125];
        Guid occurrenceId;

        using (var host = fixture.BuildHost("snapshot-before-restart"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            await host.StartAsync(ct);
            try
            {
                await fixture.SeedCronJobAsync(cronId, "snapshot", "* * * * *", NodeDeathPolicy.Retry, ct);
                await SetParentRequestAsync(originalRequest);
                var persistence = host.Services.GetRequiredService<
                    IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
                >();
                var context = new JobManagerDispatchContext(cronId)
                {
                    FunctionName = "snapshot",
                    Expression = "* * * * *",
                };
                var occurrences = await persistence
                    .QueueCronJobOccurrencesAsync((DateTime.UtcNow.AddMinutes(30), [context]), ct)
                    .ToArrayAsync(ct);
                occurrenceId = occurrences.Should().ContainSingle().Subject.Id;
                (await persistence.GetCronJobOccurrenceRequestAsync(occurrenceId, ct)).Should().Equal(originalRequest);
            }
            finally
            {
                await host.StopAsync(ct);
            }
        }

        // Change only the persisted parent payload after materialization; no identity or version edit can
        // explain a different pending execution request, and a fresh host cannot hide it in an identity map.
        await SetParentRequestAsync(replacementRequest);
        using var restartedHost = fixture.BuildHost("snapshot-after-restart");
        await restartedHost.StartAsync(ct);
        try
        {
            var restarted = restartedHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            (await restarted.GetCronJobOccurrenceRequestAsync(occurrenceId, ct)).Should().Equal(originalRequest);
        }
        finally
        {
            await restartedHost.StopAsync(ct);
        }

        async Task SetParentRequestAsync(byte[] request)
        {
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"UPDATE {fixture.QualifiedCronJobsTable} SET \"Request\" = @request WHERE \"Id\" = @id";
            var payload = command.CreateParameter();
            payload.ParameterName = "request";
            payload.Value = request;
            command.Parameters.Add(payload);
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = cronId;
            command.Parameters.Add(id);
            (await command.ExecuteNonQueryAsync(ct)).Should().Be(1);
        }
    }

    public virtual async Task synchronized_workers_claim_disjoint_time_job_roots_and_complete_descendant_stamps()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("claim-a");
        using var secondHost = fixture.BuildHost("claim-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        await firstHost.StartAsync(ct);
        await secondHost.StartAsync(ct);

        try
        {
            var first = firstHost.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var second = secondHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var executionTime = DateTime.UtcNow;
            var roots = Enumerable.Range(0, 101).Select(_ => _CreateJobTree(executionTime)).ToArray();
            await first.AddTimeJobsAsync(roots, ct);

            // Each node peeks through its OWN provider, exactly as two real nodes do. Handing both workers one
            // array would not test contention at all: a won claim mutates the caller's entities in place — owner,
            // lease, status, and UpdatedAt, which IS the optimistic-concurrency token — so the loser would rebuild
            // its candidate predicate from the winner's already-advanced token and re-match the row just claimed.
            // Peeked concurrently, not one after the other: the peek window only reaches back one second from the
            // seeded execution time, so a second serial round trip would spend that budget instead of testing it.
            var candidateSnapshots = await Task.WhenAll(
                first.GetEarliestTimeJobsAsync(ct),
                second.GetEarliestTimeJobsAsync(ct)
            );
            var (firstCandidates, secondCandidates) = (candidateSnapshots[0].Jobs, candidateSnapshots[1].Jobs);
            firstCandidates.Should().HaveCount(101);
            // Both snapshots must cover the same rows, or the disjointness assertion below would be vacuous.
            secondCandidates.Select(x => x.Id).Should().BeEquivalentTo(firstCandidates.Select(x => x.Id));

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstClaim = _ClaimTimeJobsAsync(first, firstCandidates, gate.Task, ct);
            var secondClaim = _ClaimTimeJobsAsync(second, secondCandidates, gate.Task, ct);
            gate.SetResult();
            var claims = await Task.WhenAll(firstClaim, secondClaim);

            claims.Should().Contain(x => x.Length > 0);
            var initiallyClaimedIds = claims.SelectMany(x => x).Select(x => x.Id).ToHashSet();
            // Sweeps the tail neither worker attempted (the claim batch is capped below the candidate count) through
            // this node's own snapshot; those rows were never claimed, so their peeked tokens are still current.
            var remainingCandidates = firstCandidates.Where(x => !initiallyClaimedIds.Contains(x.Id)).ToArray();
            var followUp = await first.QueueTimeJobsAsync(remainingCandidates, ct).ToArrayAsync(ct);
            var claimedRoots = claims.SelectMany(x => x).Concat(followUp).ToArray();
            claimedRoots.Should().OnlyHaveUniqueItems(x => x.Id);
            claimedRoots.Should().HaveCount(101);
            var claimedRootIds = claimedRoots.Select(x => x.Id).ToHashSet();
            foreach (var root in roots.Where(x => claimedRootIds.Contains(x.Id)))
            {
                var (_, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(root.Id, ct);
                ownerId.Should().NotBeNullOrWhiteSpace();
                lockedUntil.Should().NotBeNull();

                foreach (var descendant in root.Children.SelectMany(x => x.Children.Prepend(x)))
                {
                    var (_, descendantOwnerId, descendantLockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(
                        descendant.Id,
                        ct
                    );
                    descendantOwnerId.Should().Be(ownerId);
                    descendantLockedUntil.Should().Be(lockedUntil);
                }
            }
        }
        finally
        {
            await Task.WhenAll(firstHost.StopAsync(ct), secondHost.StopAsync(ct));
        }
    }

    public virtual async Task synchronized_workers_claim_disjoint_fallback_cron_occurrences()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("cron-a");
        using var secondHost = fixture.BuildHost("cron-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        await firstHost.StartAsync(ct);
        await secondHost.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "fallback", "* * * * *", NodeDeathPolicy.Retry, ct);
            var executionTime = DateTime.UtcNow.AddMinutes(-2);
            foreach (var index in Enumerable.Range(0, 101))
            {
                await fixture.SeedCronOccurrenceAsync(
                    Guid.NewGuid(),
                    cronId,
                    (int)JobStatus.Idle,
                    null,
                    NodeDeathPolicy.Retry,
                    null,
                    executionTime.AddMilliseconds(index),
                    ct
                );
            }
            var first = firstHost.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var second = secondHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstClaim = _ClaimFallbackCronAsync(first, gate.Task, ct);
            var secondClaim = _ClaimFallbackCronAsync(second, gate.Task, ct);
            gate.SetResult();
            var claims = await Task.WhenAll(firstClaim, secondClaim);

            claims.Should().Contain(x => x.Length > 0);
            var initiallyClaimedIds = claims.SelectMany(x => x).Select(x => x.Id).ToHashSet();
            var followUp = await first.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            var claimedOccurrences = claims.SelectMany(x => x).Concat(followUp).ToArray();
            claimedOccurrences.Should().OnlyHaveUniqueItems(x => x.Id);
            claimedOccurrences.Should().HaveCount(101);
        }
        finally
        {
            await Task.WhenAll(firstHost.StopAsync(ct), secondHost.StopAsync(ct));
        }
    }

    public virtual async Task expired_existing_cron_claim_requires_retry_policy()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("policy-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var executionTime = DateTime.UtcNow.AddMinutes(1);
            var expired = DateTime.UtcNow.AddMinutes(-1);
            var results = new Dictionary<NodeDeathPolicy, int>();
            foreach (var policy in Enum.GetValues<NodeDeathPolicy>())
            {
                var cronId = Guid.NewGuid();
                var occurrenceId = Guid.NewGuid();
                await fixture.SeedCronJobAsync(cronId, policy.ToString(), "* * * * *", policy, ct);
                await fixture.SeedCronOccurrenceAsync(
                    occurrenceId,
                    cronId,
                    (int)JobStatus.Queued,
                    "old@1",
                    policy,
                    expired,
                    executionTime.AddSeconds((int)policy),
                    ct
                );
                var context = new JobManagerDispatchContext(cronId)
                {
                    FunctionName = policy.ToString(),
                    Expression = "* * * * *",
                    OnNodeDeath = policy,
                    NextCronOccurrence = new NextCronOccurrence(
                        occurrenceId,
                        TimeProvider.System.GetUtcNow().AddMinutes(-5)
                    ),
                };
                results[policy] = await persistence
                    .QueueCronJobOccurrencesAsync((executionTime.AddSeconds((int)policy), [context]), ct)
                    .CountAsync(ct);
            }

            results[NodeDeathPolicy.Retry].Should().Be(1);
            results[NodeDeathPolicy.MarkFailed].Should().Be(0);
            results[NodeDeathPolicy.Skip].Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task direct_cron_claim_applies_the_full_acquire_predicate_matrix()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("direct-matrix-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var now = TimeProvider.System.GetUtcNow();
            var executionTime = now.UtcDateTime.AddMinutes(1);
            var expired = now.UtcDateTime.AddMinutes(-1);
            var live = now.UtcDateTime.AddMinutes(5);
            var ownerProbeCronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(ownerProbeCronId, "owner_probe", "* * * * *", NodeDeathPolicy.Retry, ct);
            var ownerProbe = await persistence
                .QueueCronJobOccurrencesAsync(
                    (
                        executionTime,
                        [
                            new JobManagerDispatchContext(ownerProbeCronId)
                            {
                                FunctionName = "owner_probe",
                                Expression = "* * * * *",
                            },
                        ]
                    ),
                    ct
                )
                .ToArrayAsync(ct);
            var currentOwner = ownerProbe.Should().ContainSingle().Which.OwnerId;
            currentOwner.Should().NotBeNullOrWhiteSpace();

            var cases = new[]
            {
                new DirectCronClaimCase("idle_unleased", JobStatus.Idle, null, NodeDeathPolicy.Retry, null, true),
                new DirectCronClaimCase(
                    "queued_unleased",
                    JobStatus.Queued,
                    "old@1",
                    NodeDeathPolicy.Retry,
                    null,
                    true
                ),
                new DirectCronClaimCase(
                    "expired_retry",
                    JobStatus.Queued,
                    "old@2",
                    NodeDeathPolicy.Retry,
                    expired,
                    true
                ),
                new DirectCronClaimCase(
                    "same_owner_live",
                    JobStatus.Queued,
                    currentOwner,
                    NodeDeathPolicy.Skip,
                    live,
                    true
                ),
                new DirectCronClaimCase(
                    "foreign_live",
                    JobStatus.Queued,
                    "foreign@1",
                    NodeDeathPolicy.Retry,
                    live,
                    false
                ),
                new DirectCronClaimCase(
                    "expired_mark_failed",
                    JobStatus.Queued,
                    "old@3",
                    NodeDeathPolicy.MarkFailed,
                    expired,
                    false
                ),
                new DirectCronClaimCase(
                    "expired_skip",
                    JobStatus.Queued,
                    "old@4",
                    NodeDeathPolicy.Skip,
                    expired,
                    false
                ),
                new DirectCronClaimCase(
                    "in_progress_unleased",
                    JobStatus.InProgress,
                    null,
                    NodeDeathPolicy.Retry,
                    null,
                    false
                ),
            };

            var contexts = new List<JobManagerDispatchContext>();
            var expectedIds = new HashSet<Guid>();
            foreach (var testCase in cases)
            {
                var cronId = Guid.NewGuid();
                var occurrenceId = Guid.NewGuid();
                await fixture.SeedCronJobAsync(cronId, testCase.Function, "* * * * *", testCase.Policy, ct);
                await fixture.SeedCronOccurrenceAsync(
                    occurrenceId,
                    cronId,
                    (int)testCase.Status,
                    testCase.OwnerId,
                    testCase.Policy,
                    testCase.LockedUntil,
                    executionTime,
                    ct
                );

                contexts.Add(
                    new JobManagerDispatchContext(cronId)
                    {
                        FunctionName = testCase.Function,
                        Expression = "* * * * *",
                        OnNodeDeath = testCase.Policy,
                        NextCronOccurrence = new NextCronOccurrence(occurrenceId, now.AddMinutes(-5)),
                    }
                );

                if (testCase.ShouldClaim)
                {
                    expectedIds.Add(occurrenceId);
                }
            }

            var claims = await persistence
                .QueueCronJobOccurrencesAsync((executionTime, contexts.ToArray()), ct)
                .ToArrayAsync(ct);

            claims.Select(x => x.Id).Should().BeEquivalentTo(expectedIds);
            foreach (var claim in claims)
            {
                claim.OwnerId.Should().Be(currentOwner);
                claim.LockedUntil.Should().BeAfter(now.UtcDateTime);
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task expired_fallback_cron_claim_requires_retry_policy()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("fallback-policy-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var expired = DateTime.UtcNow.AddMinutes(-1);
            var executionTime = DateTime.UtcNow.AddMinutes(-2);
            foreach (var policy in Enum.GetValues<NodeDeathPolicy>())
            {
                var cronId = Guid.NewGuid();
                await fixture.SeedCronJobAsync(cronId, policy.ToString(), "* * * * *", policy, ct);
                await fixture.SeedCronOccurrenceAsync(
                    Guid.NewGuid(),
                    cronId,
                    (int)JobStatus.Queued,
                    "old@1",
                    policy,
                    expired,
                    executionTime.AddSeconds((int)policy),
                    ct
                );
            }

            var claims = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);

            claims.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task many_synchronized_workers_claim_each_fallback_cron_occurrence_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("fallback-contention-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "fallback-contention", "* * * * *", NodeDeathPolicy.Retry, ct);
            var executionTime = DateTime.UtcNow.AddMinutes(-2);
            foreach (var index in Enumerable.Range(0, 100))
            {
                await fixture.SeedCronOccurrenceAsync(
                    Guid.NewGuid(),
                    cronId,
                    (int)JobStatus.Idle,
                    null,
                    NodeDeathPolicy.Retry,
                    null,
                    executionTime.AddMilliseconds(index),
                    ct
                );
            }

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = Enumerable.Range(0, 100).Select(_ => _ClaimFallbackCronAsync(persistence, gate.Task, ct));
            var claimsTask = Task.WhenAll(workers);

            gate.SetResult();
            var claims = (await claimsTask).SelectMany(x => x).ToArray();

            claims.Should().OnlyHaveUniqueItems(x => x.Id);
            claims.Should().HaveCount(100);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task incompatible_native_model_falls_back_to_ef_cas_through_production_registration()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildMappedHost<FilteredJobsDbContext>("cas-filter-a", "jobs");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<FilteredJobsDbContext>(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var visible = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "visible",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-2),
            };
            var hidden = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = FilteredJobsDbContext.HiddenFunction,
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([visible, hidden], ct);

            var claims = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            claims.Should().ContainSingle().Which.Id.Should().Be(visible.Id);
            (await fixture.ReadTimeJobDetailAsync(visible.Id, ct)).OwnerId.Should().NotBeNullOrWhiteSpace();
            (await fixture.ReadTimeJobDetailAsync(hidden.Id, ct)).OwnerId.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task compatibility_fallback_claims_at_most_one_native_sized_batch_per_sweep()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildMappedHost<FilteredJobsDbContext>("cas-bounded-a", "jobs");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<FilteredJobsDbContext>(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var executionTime = DateTime.UtcNow.AddMinutes(-5);
            var visibleRoots = Enumerable
                .Range(0, 101)
                .Select(index => _CreateJobTree(executionTime.AddMilliseconds(index)))
                .ToArray();
            var hiddenRoot = _CreateJobTree(executionTime.AddSeconds(-1));
            hiddenRoot.Function = FilteredJobsDbContext.HiddenFunction;
            await persistence.AddTimeJobsAsync([hiddenRoot, .. visibleRoots], ct);

            var firstSweep = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            var secondSweep = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            var thirdSweep = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            firstSweep.Should().HaveCount(100); // Matches the native and compatibility strategy batch ceiling.
#pragma warning disable FAA0001 // This already uses ordered Equal; the analyzer misidentifies the projected identifier assertion.
            firstSweep.Select(x => x.Id).Should().Equal(visibleRoots.Take(100).Select(x => x.Id));
#pragma warning restore FAA0001
            secondSweep.Should().ContainSingle().Which.Id.Should().Be(visibleRoots[^1].Id);
            thirdSweep.Should().BeEmpty();
            (await fixture.ReadTimeJobDetailAsync(hiddenRoot.Id, ct)).OwnerId.Should().BeNull();
            var (_, claimedOwner, claimedLease, _, _) = await fixture.ReadTimeJobDetailAsync(visibleRoots[0].Id, ct);
            foreach (var descendant in visibleRoots[0].Children.SelectMany(x => x.Children.Prepend(x)))
            {
                var (_, descendantOwner, descendantLease, _, _) = await fixture.ReadTimeJobDetailAsync(
                    descendant.Id,
                    ct
                );
                descendantOwner.Should().Be(claimedOwner);
                descendantLease.Should().Be(claimedLease);
            }

            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "cas-bounded-cron", "* * * * *", NodeDeathPolicy.Retry, ct);
            var occurrenceIds = new List<Guid>(101);
            foreach (var index in Enumerable.Range(0, 101))
            {
                var occurrenceId = Guid.NewGuid();
                occurrenceIds.Add(occurrenceId);
                await fixture.SeedCronOccurrenceAsync(
                    occurrenceId,
                    cronId,
                    (int)JobStatus.Idle,
                    null,
                    NodeDeathPolicy.Retry,
                    null,
                    executionTime.AddMilliseconds(index),
                    ct
                );
            }

            var firstCronSweep = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            var secondCronSweep = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
            var thirdCronSweep = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);

            firstCronSweep.Should().HaveCount(100);
            firstCronSweep.Select(x => x.Id).Should().Equal(occurrenceIds.Take(100));
            secondCronSweep.Should().ContainSingle().Which.Id.Should().Be(occurrenceIds[^1]);
            thirdCronSweep.Should().BeEmpty();

            var cancellableRoots = new[]
            {
                _CreateJobTree(executionTime.AddMinutes(-2)),
                _CreateJobTree(executionTime.AddMinutes(-1)),
            };
            await persistence.AddTimeJobsAsync(cancellableRoots, ct);
            using var cancellation = new CancellationTokenSource();
            await using (
                var enumerator = persistence
                    .QueueTimedOutTimeJobsAsync(cancellation.Token)
                    .GetAsyncEnumerator(cancellation.Token)
            )
            {
                (await enumerator.MoveNextAsync()).Should().BeTrue();
                await cancellation.CancelAsync();
                var moveNext = async () =>
                {
                    await enumerator.MoveNextAsync();
                };
                await moveNext.Should().ThrowAsync<OperationCanceledException>();
            }

            var afterCancellation = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);
            afterCancellation.Should().ContainSingle().Which.Id.Should().Be(cancellableRoots[^1].Id);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task cron_graph_projection_uses_distinct_dates_and_storage_side_status_aggregation()
    {
        var ct = AbortToken;
        var capture = new DashboardSqlCapture();
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildInterceptedHost("dashboard-projection-a", capture, TimeSpan.FromMinutes(5));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;
            await fixture.SeedCronJobAsync(cronId, "dashboard", "* * * * *", NodeDeathPolicy.Retry, ct);
            await fixture.SeedCronOccurrenceAsync(
                Guid.NewGuid(),
                cronId,
                (int)JobStatus.Succeeded,
                null,
                NodeDeathPolicy.Retry,
                null,
                today.AddDays(-20),
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                Guid.NewGuid(),
                cronId,
                (int)JobStatus.Succeeded,
                null,
                NodeDeathPolicy.Retry,
                null,
                today.AddHours(1),
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                Guid.NewGuid(),
                cronId,
                (int)JobStatus.Failed,
                null,
                NodeDeathPolicy.Retry,
                null,
                today,
                ct
            );
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            capture.Clear();

            var projection = await persistence.GetCronOccurrenceGraphStatusCountsAsync(cronId, today, ct);

            projection.Where(x => !x.IsRangeBoundary).Sum(x => x.Count).Should().Be(3);
            // The host's background services may issue unrelated Jobs maintenance queries after Clear(); scope the
            // assertion to the dashboard projection's CronJobOccurrences commands.
            var statements = capture
                .Statements.Where(sql => sql.Contains("CronJobOccurrences", StringComparison.Ordinal))
                .ToArray();
            statements.Should().HaveCount(2);
            statements.Should().Contain(sql => sql.Contains("DISTINCT", StringComparison.OrdinalIgnoreCase));
            statements.Should().Contain(sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
            statements.Should().Contain(sql => sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
            statements.Should().NotContain(sql => sql.Contains(" JOIN ", StringComparison.OrdinalIgnoreCase));
            statements.Should().NotContain(sql => sql.Contains("request", StringComparison.OrdinalIgnoreCase));
            statements.Should().NotContain(sql => sql.Contains("exception", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task concurrent_missing_cron_occurrence_creation_is_deduplicated()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("create-a");
        using var secondHost = fixture.BuildHost("create-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        await firstHost.StartAsync(ct);
        await secondHost.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "create", "* * * * *", NodeDeathPolicy.Retry, ct);
            var executionTime = DateTime.UtcNow.AddMinutes(1);
            var first = firstHost.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var second = secondHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstClaim = _CreateCronAsync(first, cronId, executionTime, gate.Task, ct);
            var secondClaim = _CreateCronAsync(second, cronId, executionTime, gate.Task, ct);
            gate.SetResult();
            var claims = await Task.WhenAll(firstClaim, secondClaim);

            claims.SelectMany(x => x).Should().ContainSingle();
            (await fixture.CountCronOccurrencesAsync(ct)).Should().Be(1);
        }
        finally
        {
            await Task.WhenAll(firstHost.StopAsync(ct), secondHost.StopAsync(ct));
        }
    }

    public virtual async Task deleting_a_chain_root_removes_the_whole_descendant_tree()
    {
        // The self-referential parent FK is DeleteBehavior.NoAction, so the previous root-only RemoveRange threw
        // DbUpdateException (surfacing as a 500 through the dashboard's never-throws delete API) for ANY chain
        // root with live descendants. Deletion must remove the whole subtree, deepest level first.
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("chain-delete");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var root = _CreateJobTree(DateTime.UtcNow.AddMinutes(5));
            await persistence.AddTimeJobsAsync([root], ct);
            var totalNodes = await fixture.CountTimeJobsAsync(ct);
            totalNodes.Should().BeGreaterThan(1, "the seeded tree must actually have descendants");

            var removed = await persistence.RemoveTimeJobsAsync([root.Id], ct);

            removed.Should().Be(totalNodes, "every node of the tree is deleted, not just the root");
            (await fixture.CountTimeJobsAsync(ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task concurrent_cas_claims_of_one_row_have_exactly_one_winner()
    {
        // Adversarial repro for the portable CAS strategy: its optimistic gate is expressed as a subquery over the
        // root row rather than a predicate on the updated row itself. Under READ COMMITTED, two claimants racing
        // the SAME row must still resolve to exactly one winner (the loser's re-evaluated gate must fail after it
        // unblocks on the winner's committed write). Round-loops because the interleaving is probabilistic — a
        // single shot can trivially pass even when the gate is unsound.
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("cas-race-a", useNativeClaims: false);
        using var secondHost = fixture.BuildHost("cas-race-b", useNativeClaims: false);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        await firstHost.StartAsync(ct);
        await secondHost.StartAsync(ct);

        try
        {
            var first = firstHost.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var second = secondHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();

            for (var round = 0; round < 30; round++)
            {
                var job = new TimeJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "cas-race",
                    ExecutionTime = DateTime.UtcNow,
                };
                await first.AddTimeJobsAsync([job], ct);
                var stored = await first.GetTimeJobByIdAsync(job.Id, ct);
                // Deliberately ONE shared instance here, unlike the disjoint-roots scenario above: the winner's
                // in-place write pre-refreshes the loser's CAS token, so only the acquire predicate's foreign-live-
                // lease arm can reject it. That is exactly the same-clock-tick token residue the strategy documents
                // (JobsClaimStrategy.ClaimTimeJobsAsync), and sharing is how this test reaches it. Do not "fix".
                var peeked = new TimeJobEntity { Id = job.Id, UpdatedAt = stored!.UpdatedAt };

                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                async Task<TimeJobEntity[]> claimAsync(IJobPersistenceProvider<TimeJobEntity, CronJobEntity> p)
                {
                    await gate.Task;
                    return await p.QueueTimeJobsAsync([peeked], ct).ToArrayAsync(ct);
                }

                var firstClaim = claimAsync(first);
                var secondClaim = claimAsync(second);
                gate.SetResult();
                var claims = await Task.WhenAll(firstClaim, secondClaim);

                var winners = claims.SelectMany(x => x).Count(x => x.Id == job.Id);
                winners
                    .Should()
                    .Be(
                        1,
                        "round {0}: exactly one claimant may win a single-row CAS race — two winners means the "
                            + "optimistic gate is not re-evaluated against the committed row",
                        round
                    );
            }
        }
        finally
        {
            await Task.WhenAll(firstHost.StopAsync(ct), secondHost.StopAsync(ct));
        }
    }

    /// <summary>
    /// The migration-replacement contract, and the pair of producers that makes it need a typed column. Both write
    /// <c>Skipped</c> with the identical <c>SkippedReason</c> "Cron definition updated" and owe OPPOSITE answers: the
    /// startup seeding migration retires the row without creating a replacement, so its fire is still owed, while a
    /// runtime schedule edit creates its own replacement and would double-run if the old instant re-fired. Seeding a
    /// bare <c>Skipped</c> row with a null reason — as this test once did — cannot tell them apart and so proved
    /// neither.
    /// </summary>
    /// <remarks>
    /// Runs on BOTH claim strategies. The guard lives in different code in each (raw <c>INSERT … WHERE NOT EXISTS</c>
    /// in the natives, a batched projection probe in the portable CAS path), and only the natives used to override
    /// this scenario — so the portable path violated the contract silently for as long as it has existed.
    /// </remarks>
    public virtual async Task a_migration_retired_occurrence_re_fires_while_an_edit_replaced_one_does_not()
    {
        var ct = AbortToken;

        foreach (var useNativeClaims in new[] { true, false })
        {
            await fixture.ResetDatabaseAsync(ct);
            using var host = fixture.BuildHost(
                $"terminal-dedup-{(useNativeClaims ? "native" : "cas")}",
                useNativeClaims: useNativeClaims
            );
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            await host.StartAsync(ct);

            try
            {
                var strategy = useNativeClaims ? "native" : "portable CAS";
                var persistence = host.Services.GetRequiredService<
                    IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
                >();

                // Whole seconds: PostgreSQL stores DateTime at microsecond granularity, so a tick-precision
                // execution time would never match the dedup predicate and the test would pass for the wrong reason.
                var now = DateTime.UtcNow;
                var baseInstant = new DateTime(
                    now.Year,
                    now.Month,
                    now.Day,
                    now.Hour,
                    now.Minute,
                    now.Second,
                    DateTimeKind.Utc
                );

                // Two definitions rather than two instants on one, so neither verdict can be an artefact of the
                // other's row sitting in the same window.
                var owedCronId = Guid.NewGuid();
                var replacedCronId = Guid.NewGuid();
                await fixture.SeedCronJobAsync(owedCronId, "terminal-dedup", "* * * * *", NodeDeathPolicy.Retry, ct);
                await fixture.SeedCronJobAsync(
                    replacedCronId,
                    "terminal-dedup",
                    "* * * * *",
                    NodeDeathPolicy.Retry,
                    ct
                );

                var owedSkippedId = Guid.NewGuid();
                await fixture.SeedCronOccurrenceAsync(
                    owedSkippedId,
                    owedCronId,
                    (int)JobStatus.Skipped,
                    null,
                    NodeDeathPolicy.Retry,
                    null,
                    baseInstant.AddMinutes(1),
                    ct,
                    "Cron definition updated",
                    CronOccurrenceDisposition.ReplacementOwed
                );
                var replacedSkippedId = Guid.NewGuid();
                await fixture.SeedCronOccurrenceAsync(
                    replacedSkippedId,
                    replacedCronId,
                    (int)JobStatus.Skipped,
                    null,
                    NodeDeathPolicy.Retry,
                    null,
                    baseInstant.AddMinutes(1),
                    ct,
                    "Cron definition updated",
                    CronOccurrenceDisposition.Superseded
                );

                // NextCronOccurrence is null (the earliest-available read skips terminal rows), so dispatch takes the
                // insert path — exactly the state the scheduler reaches after a cron-expression change.
                var owedClaim = await persistence
                    .QueueCronJobOccurrencesAsync((baseInstant.AddMinutes(1), [_DedupContext(owedCronId)]), ct)
                    .ToArrayAsync(ct);
                var replacedClaim = await persistence
                    .QueueCronJobOccurrencesAsync((baseInstant.AddMinutes(1), [_DedupContext(replacedCronId)]), ct)
                    .ToArrayAsync(ct);

                var occurrence = owedClaim
                    .Should()
                    .ContainSingle(
                        "on the {0} strategy the seeding migration left this instant unaccounted for",
                        strategy
                    )
                    .Subject;
                occurrence.Id.Should().NotBe(owedSkippedId);
                occurrence.Status.Should().Be(JobStatus.Queued);
                occurrence.ExecutionTime.Should().BeCloseTo(baseInstant.AddMinutes(1), TimeSpan.FromMicroseconds(1));

                replacedClaim
                    .Should()
                    .BeEmpty(
                        "on the {0} strategy the runtime edit already issued this instant's replacement — re-firing "
                            + "would double-run the edit",
                        strategy
                    );

                (await fixture.CountCronOccurrencesAsync(ct))
                    .Should()
                    .Be(3, "on the {0} strategy exactly one of the two retired instants owed a fire", strategy);
                (await fixture.ReadCronOccurrenceAsync(owedSkippedId, ct)).Status.Should().Be((int)JobStatus.Skipped);
                (await fixture.ReadCronOccurrenceAsync(replacedSkippedId, ct))
                    .Status.Should()
                    .Be((int)JobStatus.Skipped);
            }
            finally
            {
                await host.StopAsync(ct);
            }
        }
    }

    private static JobManagerDispatchContext _DedupContext(Guid cronJobId) =>
        new(cronJobId)
        {
            FunctionName = "terminal-dedup",
            Expression = "* * * * *",
            OnNodeDeath = NodeDeathPolicy.Retry,
        };

    public virtual async Task long_cron_claim_transaction_publishes_a_fresh_lease()
    {
        var ct = AbortToken;
        var leaseDuration = TimeSpan.FromSeconds(2);
        var transactionStartedAt = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var committedAt = transactionStartedAt.Add(leaseDuration).AddMilliseconds(500);
        var timeProvider = new TransactionElapsedTimeProvider(transactionStartedAt, committedAt);
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("long-claim-a", timeProvider: timeProvider, leaseDuration: leaseDuration);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            var executionTime = transactionStartedAt.UtcDateTime.AddMinutes(1);
            await fixture.SeedCronJobAsync(cronId, "long-claim", "* * * * *", NodeDeathPolicy.Retry, ct);
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var context = new JobManagerDispatchContext(cronId)
            {
                FunctionName = "long-claim",
                Expression = "* * * * *",
                OnNodeDeath = NodeDeathPolicy.Retry,
            };

            var claimed = await persistence
                .QueueCronJobOccurrencesAsync((executionTime, [context]), ct)
                .ToArrayAsync(ct);

            claimed.Should().ContainSingle();
            claimed[0].LockedUntil.Should().BeAfter(committedAt.UtcDateTime);
            claimed[0].LockedUntil.Should().Be(claimed[0].UpdatedAt.UtcDateTime.Add(leaseDuration));

            var (_, lockedUntil) = await fixture.ReadCronOccurrenceClaimAsync(claimed[0].Id, ct);
            lockedUntil.Should().Be(claimed[0].LockedUntil);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private sealed class TransactionElapsedTimeProvider(DateTimeOffset startedAt, DateTimeOffset committedAt)
        : TimeProvider
    {
        private int _reads;

        public override DateTimeOffset GetUtcNow()
        {
            return Interlocked.Increment(ref _reads) == 1 ? startedAt : committedAt;
        }
    }

    private sealed record DirectCronClaimCase(
        string Function,
        JobStatus Status,
        string? OwnerId,
        NodeDeathPolicy Policy,
        DateTime? LockedUntil,
        bool ShouldClaim
    );

    private sealed class FilteredJobsDbContext(DbContextOptions<FilteredJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options)
    {
        public const string HiddenFunction = "hidden-by-filter";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TimeJobEntity>().HasQueryFilter(x => x.Function != HiddenFunction);
        }
    }

    private sealed class DashboardSqlCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _statements = new();

        public string[] Statements => [.. _statements];

        public void Clear()
        {
            while (_statements.TryDequeue(out _)) { }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            _statements.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _statements.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static TimeJobEntity _CreateJobTree(DateTime executionTime)
    {
        var grandchild = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "grandchild",
            RunCondition = RunCondition.OnSuccess,
        };
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "child",
            RunCondition = RunCondition.OnSuccess,
            Children = [grandchild],
        };
        return new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = executionTime,
            Children = [child],
        };
    }

    private static async Task<TimeJobEntity[]> _ClaimTimeJobsAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        TimeJobEntity[] candidates,
        Task gate,
        CancellationToken ct
    )
    {
        await gate;
        return await persistence.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct);
    }

    private static async Task<CronJobOccurrenceEntity<CronJobEntity>[]> _ClaimFallbackCronAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Task gate,
        CancellationToken ct
    )
    {
        await gate;
        return await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
    }

    private static async Task<CronJobOccurrenceEntity<CronJobEntity>[]> _CreateCronAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid cronId,
        DateTime executionTime,
        Task gate,
        CancellationToken ct
    )
    {
        await gate;
        var context = new JobManagerDispatchContext(cronId) { FunctionName = "create", Expression = "* * * * *" };
        return await persistence.QueueCronJobOccurrencesAsync((executionTime, [context]), ct).ToArrayAsync(ct);
    }
}
