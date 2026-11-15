// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.Jobs.Internal;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

// Idempotent enqueue for the relational provider: the same kernel as keyed scheduling — transaction-owned
// advisory lock on the reservation identity, store statement clock for expiry, insert-or-observe inside one
// transaction — so PostgreSQL and SQL Server share identical semantics with no per-backend SQL.
internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Large enough that a steady stream of unique keys drains in a few statements per interval, small enough that one
    // statement's locks and log volume stay short.
    private const int _IdempotencySweepBatchSize = 1000;

    public async Task<JobIdempotencyEnqueueResult> AddIdempotentTimeJobAsync(
        TTimeJob job,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        TimeJobDueTimeSeeder? seeder,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(job);
        Argument.IsNotEmpty(idempotencyKey);
        JobIntentFingerprint.RejectOrdinaryMutation(job);
        JobContract.ValidateName(idempotencyKey);
        JobContract.ValidateIdempotencyTtl(idempotencyTtl);

        var (result, persisted) = await _ExecuteKeyedTransactionAsync(
                async (context, ct) =>
                {
                    // Clone so a rolled-back attempt never leaves durable stamps on the caller's entity.
                    var candidate = job.Clone();
                    var outcome = await _AddIdempotentAsync(
                            context,
                            candidate,
                            idempotencyKey,
                            idempotencyTtl,
                            seeder,
                            ct
                        )
                        .ConfigureAwait(false);
                    return (outcome, candidate);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Preserve the successful input-entity updates (mirrors the keyed kernel) so the manager's caller sees
        // the durable state. A hit replaces the pre-stamped id with the reservation's stored job id; a creation also
        // carries back the due time the store resolved.
        job.Id = result.JobId;
        if (result.Created)
        {
            job.ExecutionTime = persisted.ExecutionTime;
        }

        return result;
    }

    public async Task<int> DeleteExpiredIdempotencyReservationsAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        int deleted;

        do
        {
            // Each batch is its own autocommit statement, so DateTime.UtcNow translates to the store's clock at that
            // statement and no batch holds its row locks past its own delete. An enqueue that holds the key's lock
            // and replaces an expired row deletes it itself, so losing this race to it is harmless either way.
            await using var dbContext = await DbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            if (dbContext.Model.FindEntityType(typeof(JobIdempotencyReservationEntity))?.GetTableName() is null)
            {
                // A context that never mapped the reservation table never took an idempotent enqueue.
                return 0;
            }

            deleted = await dbContext
                .Set<JobIdempotencyReservationEntity>()
                .Where(x => x.ExpiresAt <= DateTime.UtcNow)
                .Take(_IdempotencySweepBatchSize)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            total += deleted;
        } while (deleted == _IdempotencySweepBatchSize);

        return total;
    }

    // The supplied-context kernel also serves a caller-owned transaction; it never commits or starts a transaction.
    private async Task<JobIdempotencyEnqueueResult> _AddIdempotentAsync(
        TDbContext context,
        TTimeJob job,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        TimeJobDueTimeSeeder? seeder,
        CancellationToken cancellationToken
    )
    {
        JobsIdempotencyModelConfiguration.ValidateOrdinalScope(context);
        var scopeKey = JobContract.CanonicalScopeKey(job.TenantId);
        await JobsKeyLock
            .AcquireIdempotencyAsync(
                context,
                scopeKey,
                job.Function,
                job.ContractVersion,
                idempotencyKey,
                cancellationToken
            )
            .ConfigureAwait(false);

        var reservation = await context
            .Set<JobIdempotencyReservationEntity>()
            .FindAsync([scopeKey, job.Function, job.ContractVersion, idempotencyKey], cancellationToken)
            .ConfigureAwait(false);

        var now = await JobsStoreClock.GetStatementUtcNowAsync(context, cancellationToken).ConfigureAwait(false);
        if (reservation is not null && reservation.ExpiresAt > now)
        {
            // Dedup hit: the payload is not identity; return the first caller's job without inserting.
            return new JobIdempotencyEnqueueResult(reservation.JobId, Created: false);
        }

        // Winner (no reservation, or expired): the entity's pre-stamped id becomes the reserved job id.
        if (job.Id == Guid.Empty)
        {
            job.Id = GuidGenerator.Create();
        }

        if (reservation is not null)
        {
            // Post-expiry replacement: exactly one contender reaches here per lock generation; losers observe the
            // winner's row after the winner's commit. The expired row is deleted and inserted fresh rather than
            // updated, because the fallback sweep may delete it concurrently: an update would then match no row and
            // fail the enqueue, while a delete that finds the row already gone is harmless. The predicate keeps the
            // statement from touching anything but the expired row this call observed.
            context.Entry(reservation).State = EntityState.Detached;
            await context
                .Set<JobIdempotencyReservationEntity>()
                .Where(x =>
                    x.ScopeKey == scopeKey
                    && x.Function == job.Function
                    && x.ContractVersion == job.ContractVersion
                    && x.IdempotencyKey == idempotencyKey
                    && x.ExpiresAt <= now
                )
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await context
            .Set<JobIdempotencyReservationEntity>()
            .AddAsync(
                new JobIdempotencyReservationEntity
                {
                    ScopeKey = scopeKey,
                    Function = job.Function,
                    ContractVersion = job.ContractVersion,
                    IdempotencyKey = idempotencyKey,
                    TenantId = job.TenantId,
                    JobId = job.Id,
                    ExpiresAt = now + idempotencyTtl,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // The statement clock that just decided the reservation's expiry also anchors a relative due time, so one read
        // serves both and the due time cannot predate the reservation.
        if (seeder is not null)
        {
            TimeJobDueTimeSeeding.Apply([job], seeder, now);
        }

        await context.Set<TTimeJob>().AddAsync(job, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAndStampAsync<TTimeJob, TCronJob>(cancellationToken).ConfigureAwait(false);
        return new JobIdempotencyEnqueueResult(job.Id, Created: true) { StoreUtcNow = seeder is null ? null : now };
    }
}
