// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Sql;

namespace Headless.AuditLog;

/// <summary>
/// The relational <see cref="IAuditLogRetention{TContext}"/>: turns on the purge setting the append-only trigger
/// checks, deletes the oldest expired rows, and turns the setting off again, all in one transaction on its own
/// connection.
/// </summary>
#pragma warning disable CA2100 // SQL text interpolates only validated schema and table identifiers; values are parameters.
internal sealed class RelationalAuditLogRetention<TContext>(
    RelationalAuditLogTable table,
    AuditLogPurgeStatements statements,
    TimeProvider timeProvider
) : IAuditLogRetention<TContext>
{
    public async Task<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default)
    {
        Argument.IsPositive(olderThan);
        Argument.IsPositive(limit);

        var cutoff = timeProvider.GetUtcNow() - olderThan;
        var dialect = table.Dialect;

        await using var connection = dialect.CreateConnection(table.Options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var enable = connection.CreateCommand())
        {
            enable.Transaction = transaction;
            enable.CommandText = statements.Enable;
            await enable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        int deleted;

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = statements.Delete;
            table.BindCreatedAt(delete, "Cutoff", cutoff);
            dialect.AddParameter(delete, "Limit", SqlColumnType.Int32, limit);
            deleted = Convert.ToInt32(
                await delete.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture
            );
        }

        if (statements.Disable is not null)
        {
            await using var disable = connection.CreateCommand();
            disable.Transaction = transaction;
            disable.CommandText = statements.Disable;
            await disable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return deleted;
    }
}
#pragma warning restore CA2100

/// <summary>
/// One provider's retention purge. <see cref="Delete"/> takes <c>@Cutoff</c> and <c>@Limit</c> and returns the deleted
/// count as a scalar; <see cref="Disable"/> is <see langword="null"/> when the setting ends with the transaction.
/// </summary>
internal sealed record AuditLogPurgeStatements(string Enable, string Delete, string? Disable);
