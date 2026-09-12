// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

internal sealed partial class PostgreSqlDataStorage : IMessageRevocationStorage
{
    public async ValueTask<MessageRevocationResult> RevokeAsync(
        Guid storageId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = $"""
            WITH revoked AS (
                DELETE FROM {_publishedTable}
                WHERE "Id"=@Id AND "Version"=@Version
                  AND {_TerminalRowGuardSimple}
                  AND "InlineAttempts"=0 AND "Retries"=0 AND "NextRetryAt" IS NULL
                RETURNING "Id"
            )
            SELECT CASE WHEN EXISTS (SELECT 1 FROM revoked) THEN 1
                        WHEN EXISTS (SELECT 1 FROM {_publishedTable} WHERE "Id"=@Id AND "Version"=@Version) THEN 2
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
                sqlParams:
                [
                    new NpgsqlParameter("@Id", storageId),
                    new NpgsqlParameter("@Version", messagingOptions.Value.Version),
                ],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
