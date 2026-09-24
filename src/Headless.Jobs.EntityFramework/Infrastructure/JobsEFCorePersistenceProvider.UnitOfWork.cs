// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    void ICoordinatedJobWriter<TTimeJob, TCronJob>.ValidateContext(
        IRelationalUnitOfWorkResource relationalResource,
        bool requireSavepoints
    ) => _ValidateRelationalResource(relationalResource, requireSavepoints);

#pragma warning disable MA0045 // Validation and borrowing an existing connection/transaction are synchronous; no connection is opened.
    private void _ValidateRelationalResource(
        IRelationalUnitOfWorkResource relationalResource,
        bool requireSavepoints = false
    )
    {
        using var context = _CreateCoordinatedContext(relationalResource);
        if (requireSavepoints)
        {
            _RequireKeyedSavepoints(context);
            JobsKeyedModelConfiguration.ValidateOrdinalScope<TTimeJob>(context);
        }
    }

    private TDbContext _CreateCoordinatedContext(IRelationalUnitOfWorkResource relationalResource)
    {
        var connection = relationalResource.Connection;
        var transaction = relationalResource.Transaction;
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException(
                "Atomic Jobs enlistment requires the exact live, open caller connection and its active transaction."
            );
        }

        // Finish OnConfiguring before binding: a same-database override must not replace the borrowed caller handles.
        var context = _CreateContext(new CoordinatedJobsDbContextOptions<TDbContext>(_coordinatedWriteOptions));
        try
        {
            if (!RelationalDatabaseIdentity.IsSameDatabase(context.Database.GetDbConnection(), connection))
            {
                throw new InvalidOperationException(
                    "The active unit of work's transaction belongs to another database, so this Jobs write cannot "
                        + "enlist. Use the same database, or schedule through an injected scheduler for an autonomous write."
                );
            }
            context.Database.SetDbConnection(connection, contextOwnsConnection: false);
            context.Database.UseTransaction(transaction);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
#pragma warning restore MA0045

    private static void _RequireKeyedSavepoints(TDbContext context)
    {
        if (
            context.Database.ProviderName
            is not ("Npgsql.EntityFrameworkCore.PostgreSQL" or "Microsoft.EntityFrameworkCore.SqlServer")
        )
        {
            throw new NotSupportedException("Coordinated keyed Jobs writes require PostgreSQL or SQL Server.");
        }
        if (context.Database.CurrentTransaction?.SupportsSavepoints != true)
        {
            throw new NotSupportedException(
                "Coordinated keyed Jobs writes require operation savepoints. No keyed write was attempted; use a transaction configuration that supports savepoints."
            );
        }
    }

    async Task<JobScheduleResult> ICoordinatedJobWriter<TTimeJob, TCronJob>.WriteKeyedTimeJobAsync(
        JobKey key,
        TTimeJob job,
        long? expectedGeneration,
        IRelationalUnitOfWorkResource relationalResource,
        CancellationToken cancellationToken
    )
    {
        await using var context = _CreateCoordinatedContext(relationalResource);
        return await _WithKeyedSavepointAsync(
                context,
                () => _ScheduleKeyedAsync(context, key, job, expectedGeneration, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    async Task<JobScheduleResult> ICoordinatedJobWriter<TTimeJob, TCronJob>.CancelKeyedTimeJobAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        IRelationalUnitOfWorkResource relationalResource,
        CancellationToken cancellationToken
    )
    {
        await using var context = _CreateCoordinatedContext(relationalResource);
        return await _WithKeyedSavepointAsync(
                context,
                () => _CancelKeyedAsync(context, scope, key, expectedGeneration, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<JobScheduleResult> _WithKeyedSavepointAsync(
        TDbContext context,
        Func<Task<JobScheduleResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var transaction = context.Database.CurrentTransaction!;
        _RequireKeyedSavepoints(context);

        var savepoint = "jobs_" + Guid.NewGuid().ToString("N")[..24];
        // SaveChanges' automatic savepoint starts after the superseding ExecuteUpdate. Protect the entire kernel.
        await transaction.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation().ConfigureAwait(false);
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
            return result with { IsProvisional = true };
        }
        catch (Exception failure)
        {
            // Request cancellation must not skip restoration of a partially superseded generation.
            using var rollbackBudget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await transaction.RollbackToSavepointAsync(savepoint, rollbackBudget.Token).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                throw new InvalidOperationException(
                    "The keyed Jobs operation failed and its savepoint could not be restored. The caller transaction is not recoverable here; an outer rollback and fresh unit of work are required.",
                    new AggregateException(failure, rollbackFailure)
                );
            }
            throw;
        }
    }

    async Task<JobIdempotencyEnqueueResult> ICoordinatedJobWriter<TTimeJob, TCronJob>.WriteIdempotentTimeJobAsync(
        TTimeJob job,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        IRelationalUnitOfWorkResource relationalResource,
        CancellationToken cancellationToken
    )
    {
        await using var context = _CreateCoordinatedContext(relationalResource);
        // The savepoint-wrapped kernel matches keyed scheduling: a kernel fault inside the caller's transaction
        // rolls back to the savepoint (reservation + job vanish together) without poisoning the outer transaction.
        return await _WithIdempotencySavepointAsync(
                context,
                () => _AddIdempotentAsync(context, job, idempotencyKey, idempotencyTtl, cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    // Same savepoint discipline as the keyed wrapper, typed for the idempotency result: release on success,
    // roll back to the savepoint on fault so the reservation and job vanish together, and never let a rollback
    // fault hide the original failure.
    private static async Task<JobIdempotencyEnqueueResult> _WithIdempotencySavepointAsync(
        TDbContext context,
        Func<Task<JobIdempotencyEnqueueResult>> operation,
        CancellationToken cancellationToken
    )
    {
        var transaction = context.Database.CurrentTransaction!;
        _RequireKeyedSavepoints(context);

        var savepoint = "jobs_" + Guid.NewGuid().ToString("N")[..24];
        await transaction.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation().ConfigureAwait(false);
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
            return result with { IsProvisional = true };
        }
        catch (Exception failure)
        {
            using var rollbackBudget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await transaction.RollbackToSavepointAsync(savepoint, rollbackBudget.Token).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                throw new InvalidOperationException(
                    "The idempotent Jobs enqueue failed and its savepoint could not be restored. The caller transaction is not recoverable here; an outer rollback and fresh unit of work are required.",
                    new AggregateException(failure, rollbackFailure)
                );
            }
            throw;
        }
    }
}
