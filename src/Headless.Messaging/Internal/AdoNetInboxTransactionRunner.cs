// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>
/// The inbox runner of a raw ADO.NET storage. Each attempt opens its own connection the way the storage does, so a
/// configured data source keeps its credentials and certificates, and enlists the attempt's transaction in observed
/// mode: the handler writes through that unit, and code holding only the connection reaches it through
/// <c>connection.UnitOfWork()</c>.
/// </summary>
/// <param name="createConnection">Creates an unopened connection to the storage's database.</param>
/// <param name="enlist">The provider's observed-mode enlist over the opened connection and its transaction.</param>
internal sealed class AdoNetInboxTransactionRunner(
    Func<DbConnection> createConnection,
    Func<DbConnection, DbTransaction, IUnitOfWork> enlist,
    IDeliveryCoordinationResolver coordinationResolver,
    ITransactionalInboxStorage storage,
    ILogger logger
) : InboxTransactionRunner(coordinationResolver, storage, logger)
{
    protected override async ValueTask<InboxAttemptTransaction> BeginAttemptAsync(CancellationToken cancellationToken)
    {
        var connection = createConnection();
        DbTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            transaction = await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            return new Attempt(connection, transaction, enlist(connection, transaction));
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Attempt(DbConnection connection, DbTransaction transaction, IUnitOfWork unitOfWork)
        : InboxAttemptTransaction
    {
        public override IUnitOfWork UnitOfWork => unitOfWork;

        public override DbTransaction Transaction => transaction;

        public override Task CommitAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public override Task RollbackAsync(CancellationToken cancellationToken) =>
            transaction.RollbackAsync(cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await unitOfWork.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
