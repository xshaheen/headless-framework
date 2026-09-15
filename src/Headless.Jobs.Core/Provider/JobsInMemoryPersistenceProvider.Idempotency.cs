// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Provider;

// Idempotent enqueue for the in-memory provider: the same reservation lifecycle as the relational kernel, with
// _keyedOperations playing the advisory-lock role and the injected TimeProvider playing the store clock.
internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Composite reservation identity; tuple equality is ordinal and keeps tenant scopes distinct from system scope.
    private readonly Dictionary<
        (string ScopeKey, string Function, string ContractVersion, string IdempotencyKey),
        (Guid JobId, DateTime ExpiresAt)
    > _idempotencyReservations = [];

    public Task<JobIdempotencyEnqueueResult> AddIdempotentTimeJobAsync(
        TTimeJob job,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(job);
        Argument.IsNotEmpty(idempotencyKey);
        cancellationToken.ThrowIfCancellationRequested();
        JobAtomicity.RejectDirect([job]);
        JobIntentFingerprint.RejectOrdinaryMutation(job);
        JobContract.ValidateName(idempotencyKey);

        lock (_keyedOperations)
        {
            var identity = (
                JobContract.CanonicalScopeKey(job.TenantId),
                job.Function,
                job.ContractVersion,
                idempotencyKey
            );
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            if (_idempotencyReservations.TryGetValue(identity, out var reservation) && reservation.ExpiresAt > utcNow)
            {
                // Dedup hit: the payload is not identity; return the first caller's job without inserting.
                return Task.FromResult(new JobIdempotencyEnqueueResult(reservation.JobId, Created: false));
            }

            // Winner (no reservation, or expired): atomically replace/insert the reservation keyed by the
            // entity's pre-stamped id, then insert the job. A rejected insert cannot happen inside this lock —
            // the id was validated against stored rows before any mutation (same protocol as AddTimeJobsAsync).
            _idempotencyReservations[identity] = (job.Id, utcNow + idempotencyTtl);
            var inserted = _AddTickerWithChildren(job);
            if (inserted == 0)
            {
                // A duplicate id collided with stored state: undo the reservation so the call leaves nothing behind.
                _idempotencyReservations.Remove(identity);
                throw new InvalidOperationException(
                    $"An idempotent enqueue could not insert job '{job.Function}' because its identifier already exists."
                );
            }

            return Task.FromResult(new JobIdempotencyEnqueueResult(job.Id, Created: true));
        }
    }
}
