// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Transactions;

namespace Tests.Transactions;

public sealed class OutboxTransactionExtensionsTests
{
    [Fact]
    public void begin_outbox_transaction_should_open_closed_connection_assign_transaction_and_copy_auto_commit()
    {
        // given
        var connection = new RecordingDbConnection();
        var transaction = new RecordingOutboxTransaction();

        // when
        var result = connection.BeginOutboxTransaction(IsolationLevel.Serializable, transaction, autoCommit: true);

        // then
        result.Should().BeSameAs(transaction);
        connection.OpenCount.Should().Be(1);
        connection.LastIsolationLevel.Should().Be(IsolationLevel.Serializable);
        transaction.AutoCommit.Should().BeTrue();
        transaction.DbTransaction.Should().BeSameAs(connection.LastTransaction);
    }

    [Fact]
    public async Task begin_outbox_transaction_async_should_use_async_open_and_begin_transaction_paths()
    {
        // given
        var connection = new RecordingDbConnection();
        var transaction = new RecordingOutboxTransaction();

        // when
        var result = await connection.BeginOutboxTransactionAsync(
            IsolationLevel.ReadCommitted,
            transaction,
            autoCommit: true,
            CancellationToken.None
        );

        // then
        result.Should().BeSameAs(transaction);
        connection.OpenAsyncCount.Should().Be(1);
        connection.BeginTransactionAsyncCount.Should().Be(1);
        connection.LastIsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
        transaction.AutoCommit.Should().BeTrue();
        transaction.DbTransaction.Should().BeSameAs(connection.LastTransaction);
    }

    [Fact]
    public async Task begin_outbox_transaction_async_should_propagate_open_failure_without_mutating_transaction()
    {
        // given
        var failure = new InvalidOperationException("open failed");
        var connection = new RecordingDbConnection { OpenAsyncException = failure };
        var transaction = new RecordingOutboxTransaction();

        // when
        var act = async () =>
            await connection.BeginOutboxTransactionAsync(
                IsolationLevel.Unspecified,
                transaction,
                autoCommit: true,
                CancellationToken.None
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("open failed");
        transaction.DbTransaction.Should().BeNull();
        transaction.AutoCommit.Should().BeFalse();
    }

    private sealed class RecordingOutboxTransaction : IOutboxTransaction
    {
        public bool AutoCommit { get; set; }

        public object? DbTransaction { get; set; }

        public void Commit() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Rollback() { }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private string _connectionString = "Host=localhost;Database=test";
        private ConnectionState _state = ConnectionState.Closed;

        public int OpenCount { get; private set; }

        public int OpenAsyncCount { get; private set; }

        public int BeginTransactionAsyncCount { get; private set; }

        public Exception? OpenAsyncException { get; init; }

        public IsolationLevel? LastIsolationLevel { get; private set; }

        public RecordingDbTransaction? LastTransaction { get; private set; }

        [AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "test";

        public override string DataSource => "localhost";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void Open()
        {
            OpenCount++;
            _state = ConnectionState.Open;
        }

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            OpenAsyncCount++;
            if (OpenAsyncException is not null)
            {
                throw OpenAsyncException;
            }

            _state = ConnectionState.Open;
            await Task.CompletedTask;
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            LastIsolationLevel = isolationLevel;
            LastTransaction = new RecordingDbTransaction(this, isolationLevel);
            return LastTransaction;
        }

        protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken
        )
        {
            BeginTransactionAsyncCount++;
            LastIsolationLevel = isolationLevel;
            LastTransaction = new RecordingDbTransaction(this, isolationLevel);
            return await ValueTask.FromResult<DbTransaction>(LastTransaction);
        }

        protected override DbCommand CreateDbCommand()
        {
            throw new NotSupportedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingDbTransaction(RecordingDbConnection connection, IsolationLevel isolationLevel)
        : DbTransaction
    {
        public override IsolationLevel IsolationLevel { get; } = isolationLevel;

        protected override DbConnection DbConnection { get; } = connection;

        public override void Commit() { }

        public override void Rollback() { }
    }
}
