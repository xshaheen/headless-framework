// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.PostgreSql.EntityFramework;

// The purge sets the same session setting the raw provider's trigger checks, or calls the same owner's function, so
// it keeps working on a table a migration made append-only.
internal sealed class PostgreSqlEfAuditLogRetention<TContext>(
    IDbContextFactory<TContext> dbFactory,
    IOptions<AuditLogStorageOptions> storageOptions,
    TimeProvider timeProvider
) : EfAuditLogRetention<TContext>(dbFactory, storageOptions, timeProvider)
    where TContext : DbContext
{
    // One statement, so EF runs it through the context's execution strategy by itself.
    protected override Task<int> PurgeAsOwnerAsync(
        TContext context,
        DateTime cutoff,
        int limit,
        CancellationToken cancellationToken
    )
    {
        return PostgreSqlEfAuditLogFunctions.CallAsync(
            context,
            PostgreSqlAuditLogSql.PurgeFunction,
            [cutoff, limit],
            cancellationToken
        );
    }

    // Local to the purge's transaction, so nothing has to switch it off again.
    protected override Task OpenPurgeWindowAsync(TContext context, CancellationToken cancellationToken)
    {
        return context.Database.ExecuteSqlRawAsync(
            $"SELECT pg_catalog.set_config('{AuditLogStorageNames.PurgeSetting}', 'on', true);",
            cancellationToken
        );
    }
}
