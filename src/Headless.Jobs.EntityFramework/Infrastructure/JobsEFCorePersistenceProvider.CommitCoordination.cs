// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.CommitCoordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

internal sealed partial class JobsEfCorePersistenceProvider<TDbContext, TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    void ICoordinatedJobWriter<TTimeJob, TCronJob>.ValidateContext(
        IRelationalCommitContext relationalContext,
        bool requireSavepoints
    ) => _ValidateRelationalContext(relationalContext, requireSavepoints);

#pragma warning disable MA0045 // Validation and borrowing an existing connection/transaction are synchronous; no connection is opened.
    private void _ValidateRelationalContext(IRelationalCommitContext relationalContext, bool requireSavepoints = false)
    {
        using var context = _CreateCoordinatedContext(relationalContext);
        if (requireSavepoints)
        {
            _RequireKeyedSavepoints(context);
        }
    }

    private TDbContext _CreateCoordinatedContext(IRelationalCommitContext relationalContext)
    {
        var connection = relationalContext.Connection;
        var transaction = relationalContext.Transaction;
        if (
            connection is null
            || transaction is null
            || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection)
        )
        {
            throw new InvalidOperationException(
                "Atomic Jobs enlistment requires the exact live, open caller connection and its active transaction."
            );
        }

        // Finish OnConfiguring before binding: a same-database override must not replace the borrowed caller handles.
        var context = _CreateContext(new CoordinatedJobsDbContextOptions<TDbContext>(coordinatedWriteOptions));
        try
        {
            var configured = context.Database.GetDbConnection();
            if (
                configured.GetType() != connection.GetType()
                || string.IsNullOrEmpty(configured.DataSource)
                || string.IsNullOrEmpty(configured.Database)
                || !string.Equals(configured.DataSource, connection.DataSource, StringComparison.Ordinal)
                || !string.Equals(configured.Database, connection.Database, StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "The actual configured Jobs provider, endpoint, or database differs from the caller transaction. Atomic enlistment requires an exact configured database match."
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
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    )
    {
        await using var context = _CreateCoordinatedContext(relationalContext);
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
        IRelationalCommitContext relationalContext,
        CancellationToken cancellationToken
    )
    {
        await using var context = _CreateCoordinatedContext(relationalContext);
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
}
