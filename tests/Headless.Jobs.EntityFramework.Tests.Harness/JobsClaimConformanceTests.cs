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
            var candidates = await first.GetEarliestTimeJobsAsync(ct);
            candidates.Should().HaveCount(101);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstClaim = _ClaimTimeJobsAsync(first, candidates, gate.Task, ct);
            var secondClaim = _ClaimTimeJobsAsync(second, candidates, gate.Task, ct);
            gate.SetResult();
            var claims = await Task.WhenAll(firstClaim, secondClaim);

            claims.Should().Contain(x => x.Length > 0);
            var initiallyClaimedIds = claims.SelectMany(x => x).Select(x => x.Id).ToHashSet();
            var remainingCandidates = candidates.Where(x => !initiallyClaimedIds.Contains(x.Id)).ToArray();
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

            claims.Should().OnlyContain(x => x.Length > 0);
            var claimedOccurrences = claims.SelectMany(x => x).ToArray();
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
                    NextCronOccurrence = new NextCronOccurrence(occurrenceId, DateTime.UtcNow.AddMinutes(-5)),
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

            var now = DateTime.UtcNow;
            var executionTime = now.AddMinutes(1);
            var expired = now.AddMinutes(-1);
            var live = now.AddMinutes(5);
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
                claim.LockedUntil.Should().BeAfter(now);
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
            firstSweep.Select(x => x.Id).Should().Equal(visibleRoots.Take(100).Select(x => x.Id));
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
                var enumerator = persistence.QueueTimedOutTimeJobsAsync(cancellation.Token).GetAsyncEnumerator()
            )
            {
                (await enumerator.MoveNextAsync()).Should().BeTrue();
                await cancellation.CancelAsync();
                Func<Task> moveNext = async () =>
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
            var statements = capture.Statements;
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
            claimed[0].LockedUntil.Should().Be(claimed[0].UpdatedAt.Add(leaseDuration));

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

        public string[] Statements => _statements.ToArray();

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
