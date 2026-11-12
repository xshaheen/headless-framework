// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

// The EF table comes from the application's migrations, so it carries an append-only trigger only when a migration
// added one. This class knows nothing about the database: it deletes the expired rows and nothing else. The
// database-specific packages derive from it to let that delete through their trigger, or to call the owner's function.
internal class EfAuditLogRetention<TContext>(
    IDbContextFactory<TContext> dbFactory,
    IOptions<AuditLogStorageOptions> storageOptions,
    TimeProvider timeProvider
) : IAuditLogRetention<TContext>
    where TContext : DbContext
{
    public async Task<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositive(olderThan);
        Argument.IsPositive(limit);

        var cutoff = (timeProvider.GetUtcNow() - olderThan).UtcDateTime;

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (storageOptions.Value.PurgeAuthority is AuditLogPurgeAuthority.Owner)
        {
            return await PurgeAsOwnerAsync(context, cutoff, limit, cancellationToken).ConfigureAwait(false);
        }

        // A retrying execution strategy refuses a transaction it did not start, so the whole purge is one retriable
        // unit that opens its own. A retry after a commit whose acknowledgement was lost deletes the next batch and
        // returns only that batch's count; the purge is repeated until nothing is left either way.
        return await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                (retention: this, context, cutoff, limit),
                static (state, ct) => state.retention._PurgeAsync(state.context, state.cutoff, state.limit, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Purges through the function that runs with the table owner's rights.</summary>
    /// <exception cref="NotSupportedException">The database has no owner-held purge.</exception>
    protected virtual Task<int> PurgeAsOwnerAsync(
        TContext context,
        DateTime cutoff,
        int limit,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException(
            $"{nameof(AuditLogPurgeAuthority.Owner)} purge authority is supported on PostgreSQL only."
        );
    }

    /// <summary>
    /// Runs inside the purge's transaction, before the delete: lets the delete through an append-only trigger.
    /// </summary>
    protected virtual Task OpenPurgeWindowAsync(TContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>Runs inside the purge's transaction, after the delete: undoes what opening the window left behind.</summary>
    protected virtual Task ClosePurgeWindowAsync(TContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task<int> _PurgeAsync(
        TContext context,
        DateTime cutoff,
        int limit,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await OpenPurgeWindowAsync(context, cancellationToken).ConfigureAwait(false);

        var deleted = await context
            .Set<AuditLogEntry>()
            .Where(entry => entry.CreatedAt < cutoff)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Take(limit)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await ClosePurgeWindowAsync(context, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return deleted;
    }
}
