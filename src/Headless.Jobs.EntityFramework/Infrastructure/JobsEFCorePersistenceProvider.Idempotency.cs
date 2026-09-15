// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Checks;
using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;
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
    public async Task<JobIdempotencyEnqueueResult> AddIdempotentTimeJobAsync(
        TTimeJob job,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(job);
        Argument.IsNotEmpty(idempotencyKey);
        JobAtomicity.RejectDirect([job]);
        JobIntentFingerprint.RejectOrdinaryMutation(job);
        JobContract.ValidateName(idempotencyKey);
        JobContract.ValidateIdempotencyTtl(idempotencyTtl);

        var (result, _) = await _ExecuteKeyedTransactionAsync(
                async (context, ct) =>
                {
                    // Clone so a rolled-back attempt never leaves durable stamps on the caller's entity.
                    var candidate = job.Clone();
                    var outcome = await _AddIdempotentAsync(context, candidate, idempotencyKey, idempotencyTtl, ct)
                        .ConfigureAwait(false);
                    return (outcome, candidate);
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        // Preserve the successful input-entity updates (mirrors the keyed kernel) so the manager's caller sees
        // the durable state. A hit replaces the pre-stamped id with the reservation's stored job id.
        job.Id = result.JobId;
        return result;
    }

    // The supplied-context kernel also serves a caller-owned transaction; it never commits or starts a transaction.
    private async Task<JobIdempotencyEnqueueResult> _AddIdempotentAsync(
        TDbContext context,
        TTimeJob job,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
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

        if (reservation is null)
        {
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
        }
        else
        {
            // Post-expiry replacement: exactly one contender reaches here per lock generation; losers observe
            // the winner's row after the winner's commit.
            reservation.JobId = job.Id;
            reservation.ExpiresAt = now + idempotencyTtl;
            reservation.UpdatedAt = now;
        }

        await context.Set<TTimeJob>().AddAsync(job, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new JobIdempotencyEnqueueResult(job.Id, Created: true);
    }
}
