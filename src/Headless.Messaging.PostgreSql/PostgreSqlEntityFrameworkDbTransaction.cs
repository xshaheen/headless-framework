// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging;
using Microsoft.EntityFrameworkCore.Infrastructure;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore.Storage;

internal class PostgreSqlEntityFrameworkDbTransaction : IDbContextTransaction, IInfrastructure<DbTransaction>
{
    private readonly IOutboxTransaction _transaction;

    public PostgreSqlEntityFrameworkDbTransaction(IOutboxTransaction transaction)
    {
        _transaction = transaction;
        var dbContextTransaction = (IDbContextTransaction)_transaction.DbTransaction!;
        TransactionId = dbContextTransaction.TransactionId;
    }

    public void Dispose()
    {
        _transaction.Dispose();
    }

    public void Commit()
    {
        _transaction.Commit();
    }

    public void Rollback()
    {
        _transaction.Rollback();
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return _transaction.CommitAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _transaction.RollbackAsync(cancellationToken);
    }

    public Guid TransactionId { get; }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().AnyContext();
        }
        else
        {
            _transaction.Dispose();
        }
    }

    public DbTransaction Instance
    {
        get
        {
            var dbContextTransaction = (IDbContextTransaction)_transaction.DbTransaction!;
            return dbContextTransaction.GetDbTransaction();
        }
    }
}
