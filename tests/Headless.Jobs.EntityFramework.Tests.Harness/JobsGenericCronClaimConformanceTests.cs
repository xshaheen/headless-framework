// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Fencing;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public abstract class JobsGenericCronClaimConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task existing_cron_claim_returns_confirmed_state_and_preserves_snapshot(
        bool reclaimExpiredOwner
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("generic-cron-claim", useNativeClaims: false, fastMembership: true);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var provider = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var definition = new CronJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "snapshot.original",
                ContractVersion = "1",
                Request = [1, 2, 3],
                CorrelationId = "original-root",
                CausationId = "original-cause",
                Expression = "* * * * *",
                OnNodeDeath = NodeDeathPolicy.Retry,
            };
            await provider.InsertCronJobsAsync([definition], ct);
            var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
            {
                Id = Guid.NewGuid(),
                CronJobId = definition.Id,
                ExecutionTime = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            await provider.InsertCronJobOccurrencesAsync([occurrence], ct);

            var factory = host.Services.GetRequiredService<IDbContextFactory<JobsDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var previousStatus = reclaimExpiredOwner ? JobStatus.Queued : JobStatus.Idle;
            // An expired owner's claim is an active lease that lapsed; otherwise the row was never leased.
            var previousLease = reclaimExpiredOwner
                ? new RowLeaseSnapshot(
                    1,
                    RowLeaseState.Active,
                    DateTimeOffset.UtcNow.AddHours(-1),
                    "expired-owner@1",
                    0
                )
                : default;
            await db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                .Where(x => x.Id == occurrence.Id)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.Status, previousStatus)
                            .SetProperty(x => x.Lease.Generation, previousLease.Generation)
                            .SetProperty(x => x.Lease.State, previousLease.State)
                            .SetProperty(x => x.Lease.ExpiresAt, previousLease.ExpiresAt)
                            .SetProperty(x => x.Lease.Holder, previousLease.Holder)
                            .SetProperty(x => x.RetryCount, 3),
                    ct
                );

            // The current definition differs from the occurrence's captured contract, so rebuilding the result
            // from the dispatch projection would lose both its original payload and its consumed retry budget.
            definition.Function = "snapshot.updated";
            definition.ContractVersion = "2";
            definition.Request = [4, 5, 6];
            await provider.UpdateCronJobsAsync([definition], ct);
            var dispatch = new JobManagerDispatchContext(definition.Id)
            {
                FunctionName = definition.Function,
                Expression = definition.Expression,
                ScheduleRevision = definition.ScheduleRevision,
                OnNodeDeath = NodeDeathPolicy.Retry,
                NextCronOccurrence = new NextCronOccurrence(occurrence.Id, occurrence.CreatedAt),
            };

            var claims = await provider
                .QueueCronJobOccurrencesAsync((occurrence.ExecutionTime, [dispatch]), ct)
                .ToArrayAsync(ct);

            var claimed = claims.Should().ContainSingle().Subject;
            var stored = await db.Set<CronJobOccurrenceEntity<CronJobEntity>>()
                .AsNoTracking()
                .SingleAsync(x => x.Id == occurrence.Id, ct);
            stored.Status.Should().Be(JobStatus.Queued);
            stored.Lease.State.Should().Be(RowLeaseState.Active);
            stored.Lease.Holder.Should().Contain("/generic-cron-claim@");
            claimed.Id.Should().Be(stored.Id);
            claimed.Status.Should().Be(stored.Status);
            claimed.Lease.Holder.Should().Be(stored.Lease.Holder);
            claimed.Lease.ExpiresAt.Should().NotBeNull().And.Be(stored.Lease.ExpiresAt);
            claimed.Lease.Should().Be(stored.Lease);
            claimed.UpdatedAt.Should().Be(stored.UpdatedAt);
            claimed.Function.Should().Be("snapshot.original").And.Be(stored.Function);
            claimed.ContractVersion.Should().Be("1").And.Be(stored.ContractVersion);
            claimed.Request.Should().Equal(1, 2, 3).And.Equal(stored.Request!);
            claimed.CorrelationId.Should().Be("original-root").And.Be(stored.CorrelationId);
            claimed.CausationId.Should().Be("original-cause").And.Be(stored.CausationId);
            claimed.TenantId.Should().BeNull();
            claimed.RetryCount.Should().Be(3).And.Be(stored.RetryCount);
            claimed.CronJob.Function.Should().Be("snapshot.updated");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }
}
