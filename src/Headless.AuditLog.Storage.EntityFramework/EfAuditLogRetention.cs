// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.Internal;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

// The EF table comes from the application's migrations, so it carries an append-only trigger only when a migration
// added one. The purge sets the same session setting the raw providers' trigger checks, or calls the same owner's
// function, so it keeps working when one did.
internal sealed class EfAuditLogRetention<TContext>(
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
        var provider = EfAuditLogDatabase.Provider(context);

        if (storageOptions.Value.PurgeAuthority is AuditLogPurgeAuthority.Owner)
        {
            EfAuditLogDatabase.EnsurePostgreSql(provider, nameof(AuditLogPurgeAuthority.Owner) + " purge authority");

            // One statement, so EF runs it through the context's execution strategy by itself.
            return await EfAuditLogDatabase
                .CallAsync(context, PostgreSqlAuditLogSql.PurgeFunction, [cutoff, limit], cancellationToken)
                .ConfigureAwait(false);
        }

        // A retrying execution strategy refuses a transaction it did not start, so the whole purge is one retriable
        // unit that opens its own. A retry after a commit whose acknowledgement was lost deletes the next batch and
        // returns only that batch's count; the purge is repeated until nothing is left either way.
        return await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(
                (context, provider, cutoff, limit),
                static (state, ct) => _PurgeAsync(state.context, state.provider, state.cutoff, state.limit, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<int> _PurgeAsync(
        TContext context,
        StorageProvider? provider,
        DateTime cutoff,
        int limit,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await context
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (provider is StorageProvider.PostgreSql)
        {
            await context
                .Database.ExecuteSqlRawAsync(
                    $"SELECT pg_catalog.set_config('{AuditLogStorageNames.PurgeSetting}', 'on', true);",
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        else if (provider is StorageProvider.SqlServer)
        {
            await context
                .Database.ExecuteSqlRawAsync(
                    $"EXEC sys.sp_set_session_context @key = N'{AuditLogStorageNames.PurgeSetting}', @value = 1;",
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        var deleted = await context
            .Set<AuditLogEntry>()
            .Where(entry => entry.CreatedAt < cutoff)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Take(limit)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (provider is StorageProvider.SqlServer)
        {
            // SESSION_CONTEXT outlives the transaction, so clear it before the connection returns to the pool.
            await context
                .Database.ExecuteSqlRawAsync(
                    $"EXEC sys.sp_set_session_context @key = N'{AuditLogStorageNames.PurgeSetting}', @value = NULL;",
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return deleted;
    }
}
