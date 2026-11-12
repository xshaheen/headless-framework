// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog.PostgreSql.EntityFramework;

// The partition functions come from the application's migration (AddHeadlessAuditLogPartitions). Each call is one
// statement, so EF runs it through the context's execution strategy by itself.
internal sealed class PostgreSqlEfAuditLogPartitions<TContext>(
    IDbContextFactory<TContext> dbFactory,
    IOptions<AuditLogStorageOptions> storageOptions,
    TimeProvider timeProvider
) : IAuditLogPartitions<TContext>
    where TContext : DbContext
{
    public async Task<int> EnsureAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await PostgreSqlEfAuditLogFunctions
            .CallAsync(
                context,
                PostgreSqlAuditLogSql.EnsurePartitionsFunction,
                [timeProvider.GetUtcNow().UtcDateTime, storageOptions.Value.PartitionMonthsAhead],
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<int> DropExpiredAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        Argument.IsPositive(olderThan);

        await using var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await PostgreSqlEfAuditLogFunctions
            .CallAsync(
                context,
                PostgreSqlAuditLogSql.DropPartitionsFunction,
                [(timeProvider.GetUtcNow() - olderThan).UtcDateTime],
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
