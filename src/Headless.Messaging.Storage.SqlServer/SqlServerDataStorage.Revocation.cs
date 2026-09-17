// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;

namespace Headless.Messaging.Storage.SqlServer;

internal sealed partial class SqlServerDataStorage : IMessageRevocationStorage
{
    private const string _ScheduledEligibilityPredicate =
        $"{_TerminalRowGuardSimple} AND InlineAttempts=0 AND Retries=0 AND NextRetryAt IS NULL";

    private const string _ScheduledPendingPredicate =
        "Version=@Version AND StatusName IN ('Delayed','Queued') AND InlineAttempts=0 AND Retries=0 AND NextRetryAt IS NULL AND ExpiresAt IS NOT NULL AND IntentType IN (0, 1)";

    public async ValueTask<MessageRevocationResult> RevokeAsync(
        Guid storageId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = $"""
            DECLARE @Revoked TABLE (Id uniqueidentifier);
            DELETE FROM {_publishedTable}
            OUTPUT deleted.Id INTO @Revoked
            WHERE Id=@Id AND Version=@Version
              AND {_ScheduledEligibilityPredicate};
            SELECT CASE WHEN EXISTS (SELECT 1 FROM @Revoked) THEN 1
                        WHEN EXISTS (SELECT 1 FROM {_publishedTable} WHERE Id=@Id AND Version=@Version) THEN 2
                        ELSE 0 END;
            """;
        await using var connection = new SqlConnection(options.Value.ConnectionString);
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
                    new SqlParameter("@Id", storageId),
                    new SqlParameter("@Version", messagingOptions.Value.Version),
                ],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
