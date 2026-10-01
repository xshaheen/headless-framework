// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

internal sealed partial class PostgreSqlDataStorage : IMessageRevocationStorage
{
    private const string _ScheduledEligibilityPredicate =
        $"{_TerminalRowGuardSimple} AND \"inline_attempts\"=0 AND \"retries\"=0 AND \"next_retry_at\" IS NULL";

    private const string _ScheduledPendingPredicate =
        "\"version\"=@Version AND \"status_name\" IN ('Delayed','Queued') AND \"inline_attempts\"=0 AND \"retries\"=0 AND \"next_retry_at\" IS NULL AND \"expires_at\" IS NOT NULL AND \"intent_type\" IN (0, 1)";

    public async ValueTask<MessageRevocationResult> RevokeAsync(
        Guid storageId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = $"""
            WITH revoked AS (
                DELETE FROM {_publishedTable}
                WHERE "id"=@Id AND "version"=@Version
                  AND {_ScheduledEligibilityPredicate}
                RETURNING "id"
            )
            SELECT CASE WHEN EXISTS (SELECT 1 FROM revoked) THEN 1
                        WHEN EXISTS (SELECT 1 FROM {_publishedTable} WHERE "id"=@Id AND "version"=@Version) THEN 2
                        ELSE 0 END;
            """;
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        return await connection
            .ExecuteReaderAsync(
                sql,
                async (reader, token) =>
                {
                    await reader.ReadAsync(token).ConfigureAwait(false);
                    return (MessageRevocationResult)reader.GetInt32(0);
                },
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Id", storageId), _VersionParameter()],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
