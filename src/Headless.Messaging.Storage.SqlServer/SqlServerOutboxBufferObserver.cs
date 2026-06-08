// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.AmbientTransactions;
using Headless.Messaging.Messages;
using Headless.Messaging.Storage.SqlServer.Diagnostics;
using Headless.Messaging.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Messaging.Storage.SqlServer;

internal sealed class SqlServerOutboxBufferObserver(DiagnosticProcessorObserver diagnosticProcessor)
    : IMessageOutboxBufferObserver
{
    public void MessageBuffered(IAmbientTransaction transaction, MediumMessage message)
    {
        var dbTransaction = transaction.DbTransaction as IDbTransaction;

        if (dbTransaction == null && transaction.DbTransaction is IDbContextTransaction dbContextTransaction)
        {
            dbTransaction = dbContextTransaction.GetDbTransaction();
        }

        if (dbTransaction?.Connection is not SqlConnection sqlConnection)
        {
            throw new InvalidOperationException($"{nameof(transaction.DbTransaction)} must be a SQL Server transaction.");
        }

        diagnosticProcessor.TransBuffer.TryAdd(sqlConnection.ClientConnectionId, transaction);
    }
}
