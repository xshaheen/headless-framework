// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Headless.AuditLog;

// The EF table comes from the application's migrations, so it carries an append-only trigger only when the application
// added one. The purge sets the same session setting the raw providers' trigger checks, so it keeps working when it did.
internal sealed class EfAuditLogRetention<TContext>(IDbContextFactory<TContext> dbFactory, TimeProvider timeProvider)
    : IAuditLogRetention<TContext>
    where TContext : DbContext
{
    public async Task<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositive(olderThan);
        Argument.IsPositive(limit);

        var cutoff = (timeProvider.GetUtcNow() - olderThan).UtcDateTime;

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context
            .Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var provider = _Provider(context.Database.ProviderName);

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

    private static StorageProvider? _Provider(string? providerName)
    {
        return providerName switch
        {
            HeadlessStorageNaming.NpgsqlEfProviderName => StorageProvider.PostgreSql,
            "Microsoft.EntityFrameworkCore.SqlServer" => StorageProvider.SqlServer,
            _ => null,
        };
    }
}
