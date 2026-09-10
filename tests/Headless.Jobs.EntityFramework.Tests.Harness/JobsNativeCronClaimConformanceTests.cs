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

public abstract class JobsNativeCronClaimConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task cron_claim_batches_reads_and_returns_stored_state(
        int existingCount,
        bool reclaimExpiredOwner
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var capture = new ClaimReadCapture();
        var leaseDuration = TimeSpan.FromSeconds(30);
        using var host = fixture.BuildHost(
            "native-cron-batch",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromHours(-1)),
            leaseDuration: leaseDuration,
            interceptor: capture
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const int batchSize = 8;
            var provider = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var factory = host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var executionTime = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var dispatches = new List<JobManagerDispatchContext>();
            var existingIds = new List<Guid>();
            for (var index = 0; index < batchSize; index++)
            {
                var definition = new CronJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "snapshot.original",
                    ContractVersion = "1",
                    Request = [1, 2, 3],
                    CorrelationId = "original-root",
                    CausationId = "original-cause",
                    Expression = "* * * * *",
                };
                await provider.InsertCronJobsAsync([definition], ct);
                NextCronOccurrence? next = null;
                if (index < existingCount)
                {
                    var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
                    {
                        Id = Guid.NewGuid(),
                        CronJobId = definition.Id,
                        ExecutionTime = executionTime,
                    };
                    await provider.InsertCronJobOccurrencesAsync([occurrence], ct);
                    existingIds.Add(occurrence.Id);
                    next = new NextCronOccurrence(occurrence.Id, occurrence.CreatedAt);
                }

                // Existing occurrences retain the old contract; new ones must capture the current definition.
                definition.Function = "snapshot.updated";
                definition.ContractVersion = "2";
                definition.Request = [4, 5, 6];
                await provider.UpdateCronJobsAsync([definition], ct);
                dispatches.Add(
                    new JobManagerDispatchContext(definition.Id)
                    {
                        FunctionName = definition.Function,
                        Expression = definition.Expression,
                        ScheduleRevision = definition.ScheduleRevision,
                        OnNodeDeath = NodeDeathPolicy.Retry,
                        NextCronOccurrence = next,
                    }
                );
            }

            // Public definition updates preserve lineage. Seed distinct stored values to detect read-through
            // of the definition instead of the existing occurrence's snapshot.
            await db.Set<CronJobEntity>()
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.CorrelationId, "updated-root")
                            .SetProperty(x => x.CausationId, "updated-cause"),
                    ct
                );

            var previousStatus = reclaimExpiredOwner ? JobStatus.Queued : JobStatus.Idle;
            var previousOwner = reclaimExpiredOwner ? "expired-owner@1" : null;
            var previousLease = reclaimExpiredOwner ? DateTime.UtcNow.AddHours(-1) : (DateTime?)null;
            await db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                .Where(x => existingIds.Contains(x.Id))
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.Status, previousStatus)
                            .SetProperty(x => x.OwnerId, previousOwner)
                            .SetProperty(x => x.LockedUntil, previousLease)
                            .SetProperty(x => x.RetryCount, 3),
                    ct
                );

            capture.Commands.Clear();
            var claims = await provider
                .QueueCronJobOccurrencesAsync((executionTime, [.. dispatches]), ct)
                .ToArrayAsync(ct);
            // Native lock/write commands bypass EF interception. These are precisely the EF reads that used
            // to grow per item: one optional definition batch and one joined occurrence readback.
            capture.Commands.Should().HaveCount(existingCount == batchSize ? 1 : 2);
            capture.Commands.Count(sql => sql.Contains("CronJobOccurrences", StringComparison.Ordinal)).Should().Be(1);
            claims.Should().HaveCount(batchSize);
            claims.Select(x => x.CronJobId).Should().Equal(dispatches.Select(x => x.Id));

            var stored = await db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id, ct);
            foreach (var claim in claims)
            {
                var existing = existingIds.Contains(claim.Id);
                claim.Should().BeEquivalentTo(stored[claim.Id], options => options.Excluding(x => x.CronJob));
                claim.Status.Should().Be(JobStatus.Queued);
                claim.OwnerId.Should().StartWith("native-cron-batch@");
                claim.LockedUntil.Should().Be(claim.UpdatedAt.UtcDateTime.Add(leaseDuration));
                claim.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15));
                claim.RetryCount.Should().Be(existing ? 3 : 0);
                claim.Function.Should().Be(existing ? "snapshot.original" : "snapshot.updated");
                claim.ContractVersion.Should().Be(existing ? "1" : "2");
                claim.Request.Should().Equal(existing ? [1, 2, 3] : [4, 5, 6]);
                claim.CorrelationId.Should().Be(existing ? "original-root" : "updated-root");
                claim.CausationId.Should().Be(existing ? "original-cause" : "updated-cause");
                claim.TenantId.Should().BeNull();
                claim.CronJob.Function.Should().Be("snapshot.updated");
            }
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task concurrent_cron_batches_with_opposite_order_are_deduplicated()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var firstHost = fixture.BuildHost("batch-order-a");
        using var secondHost = fixture.BuildHost("batch-order-b");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(firstHost, ct);
        await firstHost.StartAsync(ct);
        await secondHost.StartAsync(ct);
        try
        {
            var first = firstHost.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var second = secondHost.Services.GetRequiredService<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>
            >();
            var definitions = Enumerable
                .Range(0, 8)
                .Select(_ => new CronJobEntity
                {
                    Id = Guid.NewGuid(),
                    Function = "batch.order",
                    Expression = "* * * * *",
                })
                .ToArray();
            await first.InsertCronJobsAsync(definitions, ct);
            var dispatches = definitions
                .OrderBy(x => x.Id)
                .Select(x => new JobManagerDispatchContext(x.Id)
                {
                    FunctionName = x.Function,
                    Expression = x.Expression,
                    ScheduleRevision = x.ScheduleRevision,
                })
                .ToArray();
            var executionTime = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var claims = await Task.WhenAll(
                first.QueueCronJobOccurrencesAsync((executionTime, dispatches), ct).ToArrayAsync(ct).AsTask(),
                second
                    .QueueCronJobOccurrencesAsync((executionTime, [.. dispatches.OrderByDescending(x => x.Id)]), ct)
                    .ToArrayAsync(ct)
                    .AsTask()
            );
            claims.SelectMany(x => x).Select(x => x.CronJobId).Should().BeEquivalentTo(definitions.Select(x => x.Id));
            var factory = firstHost.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            (await db.Set<CronJobOccurrenceEntity<CronJobEntity>>().CountAsync(ct)).Should().Be(definitions.Length);
        }
        finally
        {
            await secondHost.StopAsync(ct);
            await firstHost.StopAsync(ct);
        }
    }

    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }

    private sealed class ClaimReadCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
