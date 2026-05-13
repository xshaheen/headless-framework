// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.PostgreSql;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

public sealed class PostgreSqlOutboxTransactionTests : TestBase
{
    [Fact]
    public void should_commit_db_transaction_and_flush_buffered_messages()
    {
        // given
        var dispatcher = new RecordingDispatcher();
        var accessor = new RecordingOutboxTransactionAccessor();
        using var transaction = new PostgreSqlOutboxTransaction(dispatcher, accessor);
        transaction.DbTransaction = new RecordingDbTransaction();
        ((IOutboxMessageBuffer)transaction).AddToSent(_CreateMessage(1));

        // when
        transaction.Commit();

        // then
        dispatcher.PublishedMessages.Should().ContainSingle();
        ((RecordingDbTransaction)transaction.DbTransaction!).CommitCount.Should().Be(1);
        accessor.Current.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task should_commit_db_context_transaction_async_and_flush_buffered_messages()
    {
        // given
        var dispatcher = new RecordingDispatcher();
        var accessor = new RecordingOutboxTransactionAccessor();
        await using var transaction = new PostgreSqlOutboxTransaction(dispatcher, accessor);
        transaction.DbTransaction = new RecordingDbContextTransaction();
        ((IOutboxMessageBuffer)transaction).AddToSent(_CreateMessage(2));

        // when
        await transaction.CommitAsync();

        // then
        dispatcher.PublishedMessages.Should().ContainSingle();
        ((RecordingDbContextTransaction)transaction.DbTransaction!).CommitAsyncCount.Should().Be(1);
    }

    [Fact]
    public void should_rollback_without_flushing_messages()
    {
        // given
        var dispatcher = new RecordingDispatcher();
        var accessor = new RecordingOutboxTransactionAccessor();
        using var transaction = new PostgreSqlOutboxTransaction(dispatcher, accessor);
        transaction.DbTransaction = new RecordingDbTransaction();
        ((IOutboxMessageBuffer)transaction).AddToSent(_CreateMessage(3));

        // when
        transaction.Rollback();

        // then
        dispatcher.PublishedMessages.Should().BeEmpty();
        ((RecordingDbTransaction)transaction.DbTransaction!).RollbackCount.Should().Be(1);
    }

    private static MediumMessage _CreateMessage(long storageId)
    {
        return new MediumMessage
        {
            StorageId = storageId,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = storageId.ToString(CultureInfo.InvariantCulture),
                    [Headers.MessageName] = $"message-{storageId}",
                },
                null
            ),
            Content = "{}",
        };
    }

    private sealed class RecordingOutboxTransactionAccessor : IOutboxTransactionAccessor
    {
        public IOutboxTransaction? Current { get; set; }
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public List<MediumMessage> PublishedMessages { get; } = [];

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default)
        {
            PublishedMessages.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        )
        {
            return ValueTask.CompletedTask;
        }

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTime publishTime,
            object? transaction = null,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }

        public ValueTask StartAsync(CancellationToken stoppingToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDbTransaction : DbTransaction
    {
        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection DbConnection => null!;

        public override void Commit()
        {
            CommitCount++;
        }

        public override void Rollback()
        {
            RollbackCount++;
        }
    }

    private sealed class RecordingDbContextTransaction : IDbContextTransaction, IInfrastructure<DbTransaction>
    {
        public int CommitAsyncCount { get; private set; }

        public Guid TransactionId { get; } = Guid.NewGuid();

        public DbTransaction Instance { get; } = new RecordingDbTransaction();

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitAsyncCount++;
            return Task.CompletedTask;
        }

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
