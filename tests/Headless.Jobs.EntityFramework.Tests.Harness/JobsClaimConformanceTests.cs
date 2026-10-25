// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Provider-neutral native claim behavior exercised exclusively through production registration.</summary>
public abstract class JobsClaimConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task reseeding_after_restart_preserves_the_stored_payload_contract(bool changeExpression)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var seed = new CronSeedDefinition(
            "seeded.contract",
            "* * * * *",
            MissedRunPolicy.Skip,
            60,
            CronOverlapPolicy.Allow,
            ContractVersion: "1"
        );
        Guid definitionId;
        using (var host = fixture.BuildHost("seed-v1"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            var provider = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await provider.MigrateDefinedCronJobsAsync([seed], ct);
            definitionId = (await provider.GetAllCronJobExpressionsAsync(ct)).Single().Id;
            var definition = (await provider.GetCronJobByIdAsync(definitionId, ct))!;
            definition.Request = [1, 2, 3];
            await provider.UpdateCronJobsAsync([definition], ct);
        }

        using var restarted = fixture.BuildHost("seed-v2");
        var restartedProvider = restarted.Services.GetRequiredService<
            IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
        >();
        var upgradedSeed = seed with
        {
            ContractVersion = "2",
            Expression = changeExpression ? "*/2 * * * *" : seed.Expression,
        };
        await restartedProvider.MigrateDefinedCronJobsAsync(
            [upgradedSeed, upgradedSeed with { Function = "new.contract" }],
            ct
        );

        var stored = (await restartedProvider.GetCronJobByIdAsync(definitionId, ct))!;
        stored.Function.Should().Be(seed.Function);
        stored.ContractVersion.Should().Be("1");
        stored.Request.Should().Equal(1, 2, 3);
        stored.Expression.Should().Be(upgradedSeed.Expression);
        (await restartedProvider.GetAllCronJobExpressionsAsync(ct))
            .Single(x => string.Equals(x.Function, "new.contract", StringComparison.Ordinal))
            .ContractVersion.Should()
            .Be("2");

        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = definitionId,
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };
        await restartedProvider.InsertCronJobOccurrencesAsync([occurrence], ct);
        var storedOccurrence = (
            await restartedProvider.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, ct)
        ).Single();
        storedOccurrence.Function.Should().Be(seed.Function);
        storedOccurrence.ContractVersion.Should().Be("1");
        (await restartedProvider.GetCronJobOccurrenceRequestAsync(occurrence.Id, ct)).Should().Equal(1, 2, 3);
    }

    public virtual async Task ordinary_job_edits_preserve_captured_lineage(string? incomingLineage)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("lineage-edits");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var provider = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var timeJob = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "causal.time",
            CorrelationId = "root",
            CausationId = "parent",
            TenantId = "tenant",
        };
        await provider.AddTimeJobsAsync([timeJob], ct);
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
            ct
        );
        var storedTime = (await provider.GetTimeJobByIdAsync(timeJob.Id, ct))!;
        storedTime.Description.Should().Be("Edited in dashboard");
        storedTime.Retries.Should().Be(4);
        storedTime.CorrelationId.Should().Be("root");
        storedTime.CausationId.Should().Be("parent");
        storedTime.TenantId.Should().Be("tenant");

        var definition = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "causal.cron",
            Expression = "* * * * *",
            CorrelationId = "root",
            CausationId = "parent",
        };
        await provider.InsertCronJobsAsync([definition], ct);
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
                    DateTimeOffset.UtcNow,
                    ct
                );
                result.Should().ContainSingle();
                result![0].CorrelationId.Should().Be("root");
                result[0].CausationId.Should().Be("parent");
            }
            else
            {
                (await provider.UpdateCronJobsAsync([edit], ct)).Should().Be(1);
            }

            var storedCron = (await provider.GetCronJobByIdAsync(definition.Id, ct))!;
            storedCron.Description.Should().Be(edit.Description);
            storedCron.Retries.Should().Be(edit.Retries);
            storedCron.CorrelationId.Should().Be("root");
            storedCron.CausationId.Should().Be("parent");
            var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = definition.Id,
                ExecutionTime = DateTime.UtcNow.AddHours(atomic ? 2 : 1),
            };
            await provider.InsertCronJobOccurrencesAsync([occurrence], ct);
            var storedOccurrence = (
                await provider.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, ct)
            ).Single();
            storedOccurrence.CorrelationId.Should().Be("root");
            storedOccurrence.CausationId.Should().Be("parent");
        }
    }

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
        using (var host = fixture.BuildHost("tuple-before-restart", fastMembership: true))
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

        using var restarted = fixture.BuildHost("tuple-after-restart", fastMembership: true);
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
            var supplementary = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = string.Concat(Enumerable.Repeat("🚀", Headless.Jobs.JobContract.NameMaxLength / 2)),
                ContractVersion = Headless.Jobs.JobContract.InitialVersion,
            };
            await provider.AddTimeJobsAsync([lower, upper, supplementary], ct);
            var roundTrip = await provider.GetTimeJobByIdAsync(supplementary.Id, ct);
            roundTrip.Should().NotBeNull();
            roundTrip!.Function.Should().Be(supplementary.Function);
            roundTrip.Request.Should().BeNull();
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
                var version = entity.FindProperty("ContractVersion")!;
                version.IsNullable.Should().BeFalse();
                version.GetDefaultValue().Should().BeNull();
                version.GetDefaultValueSql().Should().BeNull();
            }

            // Inspect and exercise the real EF-created table without the writer's value converter.
            var timeJob = context.Model.FindEntityType(typeof(TimeJobEntity))!;
            var table = StoreObjectIdentifier.Table(timeJob.GetTableName()!, timeJob.GetSchema());
            var sql = context.GetService<ISqlGenerationHelper>();
            var copiedColumns = timeJob
                .GetProperties()
                .Where(property =>
                    property.Name is not (nameof(TimeJobEntity.Id) or nameof(TimeJobEntity.ContractVersion))
                )
                .Select(property => sql.DelimitIdentifier(property.GetColumnName(table)!))
                .ToArray();
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            var sourceId = command.CreateParameter();
            sourceId.ParameterName = "@sourceId";
            sourceId.Value = lower.Id;
            command.Parameters.Add(sourceId);
            var targetId = command.CreateParameter();
            targetId.ParameterName = "@targetId";
            targetId.Value = Guid.NewGuid();
            command.Parameters.Add(targetId);
            var idColumn = sql.DelimitIdentifier(nameof(TimeJobEntity.Id));
            command.CommandText =
                $"INSERT INTO {fixture.QualifiedTimeJobsTable} ({idColumn}, {string.Join(", ", copiedColumns)}) "
                + $"SELECT @targetId, {string.Join(", ", copiedColumns)} FROM {fixture.QualifiedTimeJobsTable} WHERE {idColumn} = @sourceId";
            var omittedVersion = () => command.ExecuteNonQueryAsync(ct);
            await omittedVersion.Should().ThrowAsync<DbException>();
            command.CommandText =
                $"UPDATE {fixture.QualifiedTimeJobsTable} SET {sql.DelimitIdentifier(nameof(TimeJobEntity.ContractVersion))} = NULL WHERE {idColumn} = @sourceId";
            var nullVersion = () => command.ExecuteNonQueryAsync(ct);
            await nullVersion.Should().ThrowAsync<DbException>();

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
            command.CommandText = fixture.Sql(
                $"UPDATE {fixture.QualifiedCronJobsTable} SET \"Request\" = @request WHERE \"Id\" = @id"
            );
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
            // Due in the future, not now: the peek drops a row more than one second past due, and inserting 101 trees
            // can take longer than that on a loaded machine, which left the peek empty. A future time also keeps both
            // hosts' background schedulers from claiming a root before the workers below race for it.
            var executionTime = DateTime.UtcNow.AddSeconds(30);
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

    public virtual async Task expired_existing_cron_claim_reclaims_under_every_node_death_policy()
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

            // The occurrence never started, so its policy cannot make a reclaim run it twice.
            results.Should().AllSatisfy(result => result.Value.Should().Be(1, $"{result.Key} must be reclaimable"));
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
                new DirectClaimCase("idle_unleased", JobStatus.Idle, null, NodeDeathPolicy.Retry, null, true),
                new DirectClaimCase("queued_unleased", JobStatus.Queued, "old@1", NodeDeathPolicy.Retry, null, true),
                new DirectClaimCase("expired_retry", JobStatus.Queued, "old@2", NodeDeathPolicy.Retry, expired, true),
                new DirectClaimCase(
                    "same_owner_live",
                    JobStatus.Queued,
                    currentOwner,
                    NodeDeathPolicy.Skip,
                    live,
                    true
                ),
                new DirectClaimCase("foreign_live", JobStatus.Queued, "foreign@1", NodeDeathPolicy.Retry, live, false),
                new DirectClaimCase(
                    "expired_mark_failed",
                    JobStatus.Queued,
                    "old@3",
                    NodeDeathPolicy.MarkFailed,
                    expired,
                    true
                ),
                new DirectClaimCase("expired_skip", JobStatus.Queued, "old@4", NodeDeathPolicy.Skip, expired, true),
                new DirectClaimCase(
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
                .QueueCronJobOccurrencesAsync((executionTime, [.. contexts]), ct)
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

    /// <summary>
    /// A matching token does not make a row claimable. Each candidate carries its row's exact stored token, so only
    /// the acquire predicate separates the rows the claimer may take from the ones running, finished, or leased
    /// elsewhere. Both strategies must hold the same contract.
    /// </summary>
    public virtual async Task direct_time_job_claim_applies_the_full_acquire_predicate_matrix(bool useNativeClaims)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("direct-tj-matrix", useNativeClaims: useNativeClaims);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var probe = new TimeJobEntity { Id = Guid.NewGuid(), Function = "owner_probe" };
            await persistence.AddTimeJobsAsync([probe], ct);
            var probeClaim = await persistence.QueueTimeJobsAsync([probe], ct).ToArrayAsync(ct);
            var currentOwner = probeClaim.Should().ContainSingle().Which.OwnerId;
            currentOwner.Should().NotBeNullOrWhiteSpace();

            var now = DateTime.UtcNow;
            var expired = now.AddMinutes(-1);
            var live = now.AddMinutes(5);
            var cases = new[]
            {
                new DirectClaimCase("idle_unleased", JobStatus.Idle, null, NodeDeathPolicy.Retry, null, true),
                new DirectClaimCase("queued_unleased", JobStatus.Queued, "old@1", NodeDeathPolicy.Retry, null, true),
                new DirectClaimCase("expired_retry", JobStatus.Queued, "old@2", NodeDeathPolicy.Retry, expired, true),
                new DirectClaimCase(
                    "same_owner_live",
                    JobStatus.Queued,
                    currentOwner,
                    NodeDeathPolicy.Skip,
                    live,
                    true
                ),
                new DirectClaimCase(
                    "in_progress_foreign",
                    JobStatus.InProgress,
                    "foreign@1",
                    NodeDeathPolicy.Retry,
                    live,
                    false
                ),
                new DirectClaimCase("foreign_live", JobStatus.Queued, "foreign@2", NodeDeathPolicy.Retry, live, false),
                new DirectClaimCase(
                    "expired_mark_failed",
                    JobStatus.Queued,
                    "old@3",
                    NodeDeathPolicy.MarkFailed,
                    expired,
                    true
                ),
                new DirectClaimCase("expired_skip", JobStatus.Queued, "old@4", NodeDeathPolicy.Skip, expired, true),
                new DirectClaimCase("succeeded", JobStatus.Succeeded, "foreign@3", NodeDeathPolicy.Retry, null, false),
            };

            var candidates = new List<TimeJobEntity>();
            var expectedIds = new HashSet<Guid>();
            var refusedBefore = new Dictionary<Guid, (int Status, string? OwnerId, DateTime? LockedUntil)>();
            foreach (var testCase in cases)
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    testCase.Function,
                    (int)testCase.Status,
                    testCase.OwnerId,
                    ct,
                    testCase.Policy,
                    testCase.LockedUntil
                );
                var (_, storedToken) = await fixture.ReadTimeJobClaimTimestampsAsync(id, ct);
                candidates.Add(
                    new TimeJobEntity
                    {
                        Id = id,
                        Function = testCase.Function,
                        UpdatedAt = storedToken,
                    }
                );

                if (testCase.ShouldClaim)
                {
                    expectedIds.Add(id);
                }
                else
                {
                    var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
                    refusedBefore[id] = (status, ownerId, lockedUntil);
                }
            }

            var claims = await persistence.QueueTimeJobsAsync([.. candidates], ct).ToArrayAsync(ct);

            claims.Select(x => x.Id).Should().BeEquivalentTo(expectedIds);
            foreach (var claim in claims)
            {
                claim.OwnerId.Should().Be(currentOwner);
                claim.Status.Should().Be(JobStatus.Queued);
            }

            foreach (var (id, before) in refusedBefore)
            {
                var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
                (status, ownerId, lockedUntil).Should().Be(before);
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// The CAS fallback peeks due roots, then claims each one in its own statement. A root rescheduled between the
    /// two must not run early. An editor on a lagging clock can stamp an UpdatedAt below the peeked one, and two
    /// writes can land on the same stamp, so neither a lower nor an unchanged token may stand in for "still due":
    /// the claim re-checks the due time on the database clock. An untouched due root in the same sweep is still
    /// claimed.
    /// </summary>
    public virtual async Task cas_fallback_does_not_claim_a_root_rescheduled_after_the_peek(bool lowerStamp)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("cas-fallback-reschedule", useNativeClaims: false, fastMembership: true);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var rescheduledId = Guid.NewGuid();
        var untouchedId = Guid.NewGuid();
        foreach (var id in new[] { rescheduledId, untouchedId })
        {
            await fixture.SeedTimeJobAsync(id, "fallback-reschedule", (int)JobStatus.Idle, null, ct);
            await _ExecuteSqlAsync(
                $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ExecutionTime\" = {fixture.UtcNowOffsetSqlExpression(-60)} WHERE \"Id\" = @id;",
                ct,
                ("@id", id)
            );
        }

        var seamCalls = 0;
        var cas = new EfCoreCasJobsClaimStrategy<JobsDbContext, TimeJobEntity, CronJobEntity>(
            host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>(),
            host.Services.GetRequiredService<TimeProvider>(),
            host.Services.GetRequiredService<IGuidGenerator>(),
            new FixedOwnerIdentity("cas-fallback@1"),
            host.Services.GetRequiredService<SchedulerOptionsBuilder>()
        )
        {
            OnFallbackBeforeClaim = async job =>
            {
                Interlocked.Increment(ref seamCalls);
                if (job.Id != rescheduledId)
                {
                    return;
                }

                await _ExecuteSqlAsync(
                    $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ExecutionTime\" = {fixture.UtcNowOffsetSqlExpression(3600)}, \"UpdatedAt\" = @updatedAt WHERE \"Id\" = @id;",
                    ct,
                    ("@id", job.Id),
                    ("@updatedAt", lowerStamp ? job.UpdatedAt.AddSeconds(-1) : job.UpdatedAt)
                );
            },
        };

        var claimed = await cas.ClaimTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

        seamCalls.Should().Be(2, "both due roots were peeked before either was claimed");
        claimed.Select(x => x.Id).Should().Equal(untouchedId);
        var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(rescheduledId, ct);
        status.Should().Be((int)JobStatus.Idle);
        ownerId.Should().BeNull();
        lockedUntil.Should().BeNull();
    }

    public virtual async Task expired_fallback_claim_reclaims_an_unstarted_row_under_every_node_death_policy()
    {
        var ct = AbortToken;

        // Native and portable CAS claims must agree: a Queued row whose lease lapsed never started, so the fallback
        // reclaims it whatever its policy (a MarkFailed or Skip row whose admission write failed is otherwise stuck).
        foreach (var useNativeClaims in new[] { true, false })
        {
            var strategy = useNativeClaims ? "native" : "portable CAS";
            await fixture.ResetDatabaseAsync(ct);
            using var host = fixture.BuildHost(
                $"fallback-policy-{(useNativeClaims ? "native" : "cas")}",
                useNativeClaims: useNativeClaims
            );
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
            await host.StartAsync(ct);

            try
            {
                var persistence = host.Services.GetRequiredService<
                    IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
                >();
                var expired = DateTime.UtcNow.AddMinutes(-1);
                var executionTime = DateTime.UtcNow.AddMinutes(-2);
                var policies = Enum.GetValues<NodeDeathPolicy>();
                foreach (var policy in policies)
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
                    await fixture.SeedTimeJobAsync(
                        Guid.NewGuid(),
                        policy.ToString(),
                        (int)JobStatus.Queued,
                        "old@1",
                        ct,
                        policy,
                        expired,
                        executionTime.AddSeconds((int)policy)
                    );
                }

                var occurrences = await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct);
                var timeJobs = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

                occurrences
                    .Select(x => x.OnNodeDeath)
                    .Should()
                    .BeEquivalentTo(policies, $"the {strategy} fallback reclaims every unstarted occurrence");
                timeJobs
                    .Select(x => x.OnNodeDeath)
                    .Should()
                    .BeEquivalentTo(policies, $"the {strategy} fallback reclaims every unstarted time job");
            }
            finally
            {
                await host.StopAsync(ct);
            }
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
        using var host = fixture.BuildMappedHost<FilteredJobsDbContext>("cas-filter-a", HeadlessStorageDefaults.Schema);
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
        using var host = fixture.BuildMappedHost<FilteredJobsDbContext>(
            "cas-bounded-a",
            HeadlessStorageDefaults.Schema
        );
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
                .Statements.Where(sql =>
                    sql.Contains(fixture.JobsTable("CronJobOccurrences"), StringComparison.Ordinal)
                )
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
    /// Two nodes race to claim the same row: the node that loses records exactly one claim conflict, tagged with its
    /// claim strategy, and the winner records none. Repeated because which node wins, and whether the loser waits on
    /// the winner's lock or finds the row already taken, varies from round to round.
    /// </summary>
    public virtual async Task racing_claims_of_one_row_record_one_conflict_on_the_loser(
        bool useNativeClaims,
        string strategy
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("conflict-race-a", useNativeClaims: useNativeClaims);
        using var secondHost = fixture.BuildHost("conflict-race-b", useNativeClaims: useNativeClaims);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        await firstHost.StartAsync(ct);
        await secondHost.StartAsync(ct);
        using var conflicts = new ClaimConflictRecorder();

        try
        {
            var first = firstHost.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var second = secondHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();

            for (var round = 0; round < 5; round++)
            {
                conflicts.Clear();
                var job = new TimeJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "conflict-race",
                    ExecutionTime = DateTime.UtcNow,
                };
                await first.AddTimeJobsAsync([job], ct);

                // Each node claims from its own peek of the row, as two schedulers polling the same store do.
                var firstPeek = (await first.GetTimeJobByIdAsync(job.Id, ct))!;
                var secondPeek = (await second.GetTimeJobByIdAsync(job.Id, ct))!;
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                async Task<int> claimAsync(
                    string node,
                    IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
                    TimeJobEntity peek
                )
                {
                    ClaimConflictRecorder.Claimant.Value = node;
                    await gate.Task;
                    return (await persistence.QueueTimeJobsAsync([peek], ct).ToArrayAsync(ct)).Length;
                }

                var firstClaim = claimAsync("a", first, firstPeek);
                var secondClaim = claimAsync("b", second, secondPeek);
                gate.SetResult();
                var claimed = await Task.WhenAll(firstClaim, secondClaim);

                claimed.Sum().Should().Be(1, "round {0}: exactly one node wins the row", round);
                var loser = claimed[0] == 0 ? "a" : "b";
                var recorded = conflicts.Recorded.Should().ContainSingle("round {0}: only the loser lost a row", round);
                recorded.Subject.Claimant.Should().Be(loser);
                recorded.Subject.Value.Should().Be(1);
                recorded.Subject.Strategy.Should().Be(strategy);
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

    /// <summary>
    /// A full candidate page (the claim strategy's 1,000-job ceiling) is bound as one list of (id, UpdatedAt) rows, not
    /// two parameters per job, so it stays far from SQL Server's 2,100-parameter limit. Each claim takes one batch;
    /// repeating it over the still-idle rows claims every job exactly once.
    /// </summary>
    public virtual async Task direct_claim_of_a_full_candidate_page_claims_every_job_once()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        // Seeding and claiming a full page can stall a heartbeat past the fixture's second-scale dead threshold on a
        // loaded run; the dead-owner reclaim would then hand this live node's own claims back to Idle.
        using var host = fixture.BuildHost("page-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var jobs = Enumerable
                .Range(0, JobsClaimStrategyDefaults.MaxCandidatePageSize)
                .Select(i => new TimeJobEntity { Id = Guid.NewGuid(), Function = $"page-{i}" })
                .ToArray();
            await persistence.AddTimeJobsAsync(jobs, ct);
            var ids = jobs.Select(x => x.Id).ToHashSet();

            var claimedIds = new List<Guid>();
            var firstClaim = true;
            while (claimedIds.Count < jobs.Length)
            {
                var candidates = await persistence.GetTimeJobsAsync(
                    x => ids.Contains(x.Id) && x.Status == JobStatus.Idle,
                    ct
                );
                if (firstClaim)
                {
                    candidates.Should().HaveCount(JobsClaimStrategyDefaults.MaxCandidatePageSize);
                    firstClaim = false;
                }

                var claimed = await persistence.QueueTimeJobsAsync(candidates, ct).ToArrayAsync(ct);
                claimed.Should().HaveCount(Math.Min(JobsClaimStrategyDefaults.MaxClaimBatchSize, candidates.Length));
                claimedIds.AddRange(claimed.Select(x => x.Id));
            }

            claimedIds.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
            var stored = await persistence.GetTimeJobsAsync(x => ids.Contains(x.Id), ct);
            stored.Should().OnlyContain(x => x.Status == JobStatus.Queued && x.OwnerId != null);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// The peek reads at most one claim candidate page, the same rows the native claim keeps: the first page in the
    /// store's (ExecutionTime, Id) order.
    /// The expected order is read back with the store's own ORDER BY because each engine sorts its uuid type
    /// differently from <see cref="Guid" />.
    /// </summary>
    public virtual async Task peek_returns_at_most_one_candidate_page_in_store_order()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("peek-page-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            // Ahead of now, so the rows stay inside the peek's window however long the seeding takes; spread across
            // one second so the execution time, not only the id, decides the page.
            var now = DateTime.UtcNow.AddSeconds(30);
            var second = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
            var jobs = Enumerable
                .Range(0, JobsClaimStrategyDefaults.MaxCandidatePageSize + 1)
                .Select(i => new TimeJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "peek-page",
                    ExecutionTime = second.AddMilliseconds(i % 4 * 100),
                })
                .ToArray();
            await persistence.AddTimeJobsAsync(jobs, ct);

            var storeOrder = new List<Guid>(jobs.Length);
            await using (var connection = fixture.CreateConnection())
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = fixture.Sql(
                    $"SELECT \"Id\" FROM {fixture.QualifiedTimeJobsTable} WHERE \"Function\" = 'peek-page' "
                        + "ORDER BY \"ExecutionTime\", \"Id\";"
                );
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    storeOrder.Add(reader.GetGuid(0));
                }
            }

            storeOrder.Should().HaveCount(jobs.Length);

            var page = (await persistence.GetEarliestTimeJobsAsync(ct)).Jobs;

            page.Select(x => x.Id).Should().Equal(storeOrder.Take(JobsClaimStrategyDefaults.MaxCandidatePageSize));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// The direct claim matches each job's id and expected UpdatedAt as one row. Two candidates carrying each other's
    /// stamps must both be refused: every id and every stored stamp appears in the request, which is all two
    /// independent lists (ids, stamps) would check, but never in the same row.
    /// </summary>
    public virtual async Task direct_claim_does_not_match_a_stamp_from_another_candidate()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("pairs-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            // Whole-second stamps, so every engine stores them unchanged and each row carries its own.
            var stampBase = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await persistence.AddTimeJobsAsync(
                [
                    .. ids.Select(
                        (id, i) =>
                            new TimeJobEntity
                            {
                                Id = id,
                                Function = "pairs",
                                UpdatedAt = stampBase.AddSeconds(i),
                            }
                    ),
                ],
                ct
            );

            var first = (await persistence.GetTimeJobsAsync(x => x.Id == ids[0], ct)).Single();
            var second = (await persistence.GetTimeJobsAsync(x => x.Id == ids[1], ct)).Single();
            var matching = (await persistence.GetTimeJobsAsync(x => x.Id == ids[2], ct)).Single();
            first.UpdatedAt.Should().NotBe(second.UpdatedAt);
            (first.UpdatedAt, second.UpdatedAt) = (second.UpdatedAt, first.UpdatedAt);

            var claimed = await persistence.QueueTimeJobsAsync([first, second, matching], ct).ToArrayAsync(ct);

            claimed.Select(x => x.Id).Should().Equal(matching.Id);
            foreach (var refused in new[] { ids[0], ids[1] })
            {
                var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(refused, ct);
                status.Should().Be((int)JobStatus.Idle);
                ownerId.Should().BeNull();
                lockedUntil.Should().BeNull();
            }
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

    private sealed record DirectClaimCase(
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

    private async Task _ExecuteSqlAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(sql);
        foreach (var (name, value) in parameters)
        {
            JobsCoordinationFixtureExtensions.AddParameter(command, name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    // A stable owner with never-lost membership, so a strategy built outside the host claims without waiting for the
    // coordination-backed identity.
    private sealed class FixedOwnerIdentity(string owner) : IJobsOwnerIdentity
    {
        public string DisplayOwner => owner;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? stampOwner)
        {
            stampOwner = owner;
            return true;
        }

        public CancellationToken MembershipLostToken => CancellationToken.None;
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

    /// <summary>
    /// Records the Jobs claim-conflict counter, attributed to the claimant whose call flow emitted it. The attribution
    /// rides an async-local so measurements from other tests running in parallel in this process are ignored.
    /// </summary>
    private sealed class ClaimConflictRecorder : IDisposable
    {
        private readonly System.Diagnostics.Metrics.MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Claimant, long Value, string? Strategy)> _recorded = new();

        public static AsyncLocal<string?> Claimant { get; } = new();

        public ClaimConflictRecorder()
        {
            _listener.InstrumentPublished = static (instrument, listener) =>
            {
                if (
                    string.Equals(instrument.Meter.Name, "Headless.Jobs", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "headless.jobs.claim.conflicts", StringComparison.Ordinal)
                )
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, value, tags, _) =>
                {
                    if (Claimant.Value is not { } claimant)
                    {
                        return;
                    }

                    string? strategy = null;
                    foreach (var tag in tags)
                    {
                        if (string.Equals(tag.Key, "headless.jobs.claim.strategy", StringComparison.Ordinal))
                        {
                            strategy = tag.Value as string;
                        }
                    }

                    _recorded.Enqueue((claimant, value, strategy));
                }
            );
            _listener.Start();
        }

        public IReadOnlyCollection<(string Claimant, long Value, string? Strategy)> Recorded => [.. _recorded];

        public void Clear() => _recorded.Clear();

        public void Dispose() => _listener.Dispose();
    }
}
