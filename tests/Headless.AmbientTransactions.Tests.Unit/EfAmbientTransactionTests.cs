// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.AmbientTransactions;
using Headless.AmbientTransactions.EntityFramework;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class EfAmbientTransactionTests
{
    [Fact]
    public async Task commit_async_should_commit_ef_transaction_and_drain_registered_work()
    {
        // given
        await using var transaction = new EfAmbientTransaction(
            new RecordingDbContextTransaction(new RecordingDbTransaction(new RecordingDbConnection(), IsolationLevel.ReadCommitted)),
            new AsyncLocalCurrentAmbientTransaction()
        );
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        // when
        await transaction.CommitAsync();

        // then
        ((RecordingDbContextTransaction)transaction.DbTransaction!).CommitAsyncCount.Should().Be(1);
        drainCount.Should().Be(1);
    }

    [Fact]
    public void rollback_should_rollback_ef_transaction_and_discard_registered_work()
    {
        // given
        using var transaction = new EfAmbientTransaction(
            new RecordingDbContextTransaction(new RecordingDbTransaction(new RecordingDbConnection(), IsolationLevel.ReadCommitted)),
            new AsyncLocalCurrentAmbientTransaction()
        );
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        // when
        transaction.Rollback();
        transaction.CompleteExternally();

        // then
        ((RecordingDbContextTransaction)transaction.DbTransaction!).RollbackCount.Should().Be(1);
        drainCount.Should().Be(0);
    }

    [Fact]
    public void as_ambient_transaction_should_attach_current_and_copy_auto_commit()
    {
        // given
        var current = new AsyncLocalCurrentAmbientTransaction();
        var dbContextTransaction = new RecordingDbContextTransaction(
            new RecordingDbTransaction(new RecordingDbConnection(), IsolationLevel.ReadCommitted)
        );

        // when
        var transaction = dbContextTransaction.AsAmbientTransaction(current, autoCommit: true);

        // then
        transaction.DbTransaction.Should().BeSameAs(dbContextTransaction);
        transaction.AutoCommit.Should().BeTrue();
        current.Current.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task setup_should_register_current_accessor()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddEntityFrameworkAmbientTransactions();
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ICurrentAmbientTransaction>().Should().BeOfType<AsyncLocalCurrentAmbientTransaction>();
    }

    private sealed class RecordingDbContextTransaction(DbTransaction dbTransaction)
        : IDbContextTransaction,
            IInfrastructure<DbTransaction>
    {
        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int CommitAsyncCount { get; private set; }

        public int RollbackAsyncCount { get; private set; }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public DbTransaction Instance { get; } = dbTransaction;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
    }
}
