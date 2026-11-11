// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.SqlServer.EntityFramework;

// The purge sets the same session key the raw provider's trigger checks, so it keeps working on a table a migration
// made append-only.
internal sealed class SqlServerEfAuditLogRetention<TContext>(
    IDbContextFactory<TContext> dbFactory,
    IOptions<AuditLogStorageOptions> storageOptions,
    TimeProvider timeProvider
) : EfAuditLogRetention<TContext>(dbFactory, storageOptions, timeProvider)
    where TContext : DbContext
{
    protected override Task OpenPurgeWindowAsync(TContext context, CancellationToken cancellationToken)
    {
        return context.Database.ExecuteSqlRawAsync(
            $"EXEC sys.sp_set_session_context @key = N'{AuditLogStorageNames.PurgeSetting}', @value = 1;",
            cancellationToken
        );
    }

    // SESSION_CONTEXT outlives the transaction, so clear it before the connection returns to the pool.
    protected override Task ClosePurgeWindowAsync(TContext context, CancellationToken cancellationToken)
    {
        return context.Database.ExecuteSqlRawAsync(
            $"EXEC sys.sp_set_session_context @key = N'{AuditLogStorageNames.PurgeSetting}', @value = NULL;",
            cancellationToken
        );
    }
}
