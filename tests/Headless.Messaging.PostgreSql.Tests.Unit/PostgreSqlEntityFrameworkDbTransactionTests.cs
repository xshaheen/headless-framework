// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

public sealed class PostgreSqlEntityFrameworkDbTransactionTests : TestBase
{
    [Fact]
    public void should_forward_commit_rollback_dispose_and_transaction_id()
    {
        // given
        var innerDbTransaction = new RecordingDbTransaction();
        var innerTransaction = new RecordingDbContextTransaction(innerDbTransaction);
        var outboxTransaction = new RecordingOutboxTransaction { DbTransaction = innerTransaction };
        var sut = _CreateWrapper(outboxTransaction);

        // when
        sut.Commit();
        sut.Rollback();
        sut.Dispose();

        // then
        outboxTransaction.CommitCount.Should().Be(1);
        outboxTransaction.RollbackCount.Should().Be(1);
        outboxTransaction.DisposeCount.Should().Be(1);
        sut.TransactionId.Should().Be(innerTransaction.TransactionId);
        ((IInfrastructure<DbTransaction>)sut).Instance.Should().BeSameAs(innerDbTransaction);
    }

    [Fact]
    public async Task should_forward_async_operations()
    {
        // given
        var innerTransaction = new RecordingDbContextTransaction(new RecordingDbTransaction());
        var outboxTransaction = new RecordingOutboxTransaction { DbTransaction = innerTransaction };
        var sut = _CreateWrapper(outboxTransaction);

        // when
        await sut.CommitAsync();
        await sut.RollbackAsync();
        await sut.DisposeAsync();

        // then
        outboxTransaction.CommitAsyncCount.Should().Be(1);
        outboxTransaction.RollbackAsyncCount.Should().Be(1);
        outboxTransaction.DisposeAsyncCount.Should().Be(1);
    }

    private static IDbContextTransaction _CreateWrapper(RecordingOutboxTransaction transaction)
    {
        var wrapperType = typeof(PostgreSqlOptions).Assembly.GetType(
            "Microsoft.EntityFrameworkCore.Storage.PostgreSqlEntityFrameworkDbTransaction",
            throwOnError: true
        )!;

        return (IDbContextTransaction)Activator.CreateInstance(wrapperType, transaction)!;
    }

    private sealed class RecordingOutboxTransaction : IOutboxTransaction
    {
        public bool AutoCommit { get; set; }

        public object? DbTransaction { get; set; }

        public int CommitCount { get; private set; }

        public int CommitAsyncCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int RollbackAsyncCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int DisposeAsyncCount { get; private set; }

        public void Commit()
        {
            CommitCount++;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitAsyncCount++;
            return Task.CompletedTask;
        }

        public void Rollback()
        {
            RollbackCount++;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackAsyncCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCount++;
        }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDbContextTransaction(DbTransaction dbTransaction)
        : IDbContextTransaction,
            IInfrastructure<DbTransaction>
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public DbTransaction Instance { get; } = dbTransaction;

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;

        protected override DbConnection DbConnection => null!;

        public override void Commit() { }

        public override void Rollback() { }
    }
}
