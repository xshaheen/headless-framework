// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Headless.Messaging.Internal;
using Microsoft.Data.SqlClient;

namespace Headless.Messaging.SqlServer.Diagnostics;

internal class DiagnosticObserver(ConcurrentDictionary<Guid, SqlServerOutboxTransaction> bufferTrans)
    : IObserver<KeyValuePair<string, object?>>
{
    public const string SqlAfterCommitTransactionMicrosoft = "Microsoft.Data.SqlClient.WriteTransactionCommitAfter";
    public const string SqlErrorCommitTransactionMicrosoft = "Microsoft.Data.SqlClient.WriteTransactionCommitError";
    public const string SqlAfterRollbackTransactionMicrosoft = "Microsoft.Data.SqlClient.WriteTransactionRollbackAfter";
    public const string SqlBeforeCloseConnectionMicrosoft = "Microsoft.Data.SqlClient.WriteConnectionCloseBefore";

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void OnNext(KeyValuePair<string, object?> evt)
    {
        switch (evt.Key)
        {
            case SqlAfterCommitTransactionMicrosoft:
            {
                if (!_TryGetSqlConnection(evt, out var sqlConnection))
                {
                    return;
                }

                var transactionKey = sqlConnection.ClientConnectionId;

                if (bufferTrans.TryRemove(transactionKey, out var transaction))
                {
                    if (_GetProperty(evt.Value, "Operation") as string == "Rollback")
                    {
                        transaction.Dispose();
                        return;
                    }

                    transaction.DbTransaction = new NoopTransaction();
                    transaction.Commit();
                    transaction.Dispose();
                }

                break;
            }
            case SqlErrorCommitTransactionMicrosoft
            or SqlAfterRollbackTransactionMicrosoft
            or SqlBeforeCloseConnectionMicrosoft:
            {
                if (!bufferTrans.IsEmpty)
                {
                    if (!_TryGetSqlConnection(evt, out var sqlConnection))
                    {
                        return;
                    }

                    var transactionKey = sqlConnection.ClientConnectionId;

                    if (bufferTrans.TryRemove(transactionKey, out var transaction))
                    {
                        transaction.Dispose();
                    }
                }

                break;
            }
        }
    }

    private static bool _TryGetSqlConnection(
        KeyValuePair<string, object?> evt,
        [NotNullWhen(true)] out SqlConnection? sqlConnection
    )
    {
        sqlConnection = _GetProperty(evt.Value, "Connection") as SqlConnection;
        return sqlConnection != null;
    }

    private static object? _GetProperty(object? @this, string propertyName)
    {
        return @this?.GetType().GetTypeInfo().GetDeclaredProperty(propertyName)?.GetValue(@this);
    }
}
