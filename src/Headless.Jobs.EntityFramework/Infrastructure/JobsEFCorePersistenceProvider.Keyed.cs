// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public async Task<JobScheduleResult> ScheduleKeyedTimeJobAsync(
        JobKey key,
        TTimeJob job,
        long? expectedGeneration = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = await _ScheduleKeyedAsync(context, key, job, expectedGeneration, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // The supplied-context kernel also serves a caller-owned transaction; it never commits or starts a transaction.
    private async Task<JobScheduleResult> _ScheduleKeyedAsync(
        TDbContext context,
        JobKey key,
        TTimeJob job,
        long? expectedGeneration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration ?? 1, nameof(expectedGeneration));
        JobIntentFingerprint.RejectOrdinaryMutation(job);
        JobIntentFingerprint.Normalize(job);
        JobsKeyedModelConfiguration.ValidateOrdinalScope<TTimeJob>(context);
        var scope = new JobKeyScope(job.Function, job.TenantId);
        await JobsKeyLock.AcquireAsync(context, scope, key, cancellationToken).ConfigureAwait(false);
        var current = await _CurrentKey(context, scope, key)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (current is not null && expectedGeneration is null)
        {
            var fingerprint = JobIntentFingerprint.Compute(job, current.FingerprintAlgorithm!);
            return JobIntentFingerprint.Result(
                current,
                string.Equals(fingerprint, current.IntentFingerprint, StringComparison.Ordinal)
                    ? JobScheduleDisposition.Existing
                    : JobScheduleDisposition.Conflict
            );
        }

        if (expectedGeneration is not null)
        {
            if (current is null)
            {
                return JobIntentFingerprint.Result<TTimeJob>(job: null, JobScheduleDisposition.NotFound);
            }

            if (current.Generation != expectedGeneration)
            {
                return JobIntentFingerprint.Result(current, JobScheduleDisposition.StaleGeneration);
            }
        }

        job.Id = job.Id == Guid.Empty ? GuidGenerator.Create() : job.Id;
        await JobsKeyLock
            .AcquireRunsAsync(context, current is null ? [job.Id] : [job.Id, current.Id], cancellationToken)
            .ConfigureAwait(false);
        var now = new DateTimeOffset(
            await JobsStoreClock.GetStatementUtcNowAsync(context, cancellationToken).ConfigureAwait(false),
            TimeSpan.Zero
        );
        if (current is not null)
        {
            var affected = await context
                .Set<TTimeJob>()
                .Where(row =>
                    row.Id == current.Id
                    && row.IsCurrentGeneration == true
                    && row.Generation == expectedGeneration
                    && row.Status == JobStatus.Idle
                    && row.OwnerId == null
                    && row.LockedUntil == null
                    && !row.CancelRequested
                )
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(row => row.IsCurrentGeneration, valueExpression: false)
                            .SetProperty(row => row.Status, JobStatus.Skipped)
                            .SetProperty(row => row.SkippedReason, "Superseded by a newer keyed generation.")
                            .SetProperty(row => row.ExecutedAt, now)
                            .SetProperty(row => row.UpdatedAt, now),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return JobIntentFingerprint.Result(
                    await _CurrentKey(context, scope, key).SingleAsync(cancellationToken).ConfigureAwait(false),
                    JobScheduleDisposition.Conflict
                );
            }
        }

        job.BusinessKey = key.Value;
        job.IntentFingerprint = JobIntentFingerprint.Compute(job, JobIntentFingerprint.Algorithm);
        job.FingerprintAlgorithm = JobIntentFingerprint.Algorithm;
        job.Generation = checked((current?.Generation ?? 0) + 1);
        job.IsCurrentGeneration = true;
        job.Status = JobStatus.Idle;
        job.OwnerId = null;
        job.LockedUntil = null;
        job.CreatedAt = job.UpdatedAt = now;
        await context.Set<TTimeJob>().AddAsync(job, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return JobIntentFingerprint.Result(
            job,
            current is null ? JobScheduleDisposition.Created : JobScheduleDisposition.Replaced
        );
    }

    public async Task<JobScheduleResult> CancelKeyedTimeJobAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await DbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = await _CancelKeyedAsync(context, scope, key, expectedGeneration, cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<JobScheduleResult> _CancelKeyedAsync(
        TDbContext context,
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration);
        JobsKeyedModelConfiguration.ValidateOrdinalScope<TTimeJob>(context);
        await JobsKeyLock.AcquireAsync(context, scope, key, cancellationToken).ConfigureAwait(false);
        var current = await _CurrentKey(context, scope, key)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return JobIntentFingerprint.Result<TTimeJob>(job: null, JobScheduleDisposition.NotFound);
        }

        if (current.Generation != expectedGeneration)
        {
            return JobIntentFingerprint.Result(current, JobScheduleDisposition.StaleGeneration);
        }

        var now = new DateTimeOffset(
            await JobsStoreClock.GetStatementUtcNowAsync(context, cancellationToken).ConfigureAwait(false),
            TimeSpan.Zero
        );
        var pending = await _CurrentKey(context, scope, key)
            .Where(row =>
                row.Generation == expectedGeneration
                && row.Status == JobStatus.Idle
                && row.OwnerId == null
                && row.LockedUntil == null
            )
            .ExecuteUpdateAsync(
                setter =>
                    setter
                        .SetProperty(row => row.CancelRequested, valueExpression: true)
                        .SetProperty(row => row.Status, JobStatus.Cancelled)
                        .SetProperty(row => row.ExecutedAt, now)
                        .SetProperty(row => row.UpdatedAt, now),
                cancellationToken
            )
            .ConfigureAwait(false);
        var requested = 0;
        if (pending == 0)
        {
            requested = await _CurrentKey(context, scope, key)
                .Where(row =>
                    row.Generation == expectedGeneration
                    && (
                        row.Status == JobStatus.Idle
                        || row.Status == JobStatus.Queued
                        || row.Status == JobStatus.InProgress
                    )
                )
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(row => row.CancelRequested, valueExpression: true)
                            .SetProperty(row => row.UpdatedAt, now),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        var observed = await _CurrentKey(context, scope, key).SingleAsync(cancellationToken).ConfigureAwait(false);
        return JobIntentFingerprint.Result(
            observed,
            pending == 1 ? JobScheduleDisposition.Cancelled
                : requested == 1 ? JobScheduleDisposition.CancellationRequested
                : JobScheduleDisposition.Terminal
        );
    }

    private static IQueryable<TTimeJob> _CurrentKey(TDbContext context, JobKeyScope scope, JobKey key) =>
        context
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(row =>
                row.TenantId == scope.TenantId
                && row.Function == scope.Function
                && row.BusinessKey == key.Value
                && row.IsCurrentGeneration == true
            );
}
