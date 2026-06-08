// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.AmbientTransactions;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class AmbientTransactionExtensionsTests
{
    [Fact]
    public void begin_ambient_transaction_should_open_closed_connection_assign_transaction_and_copy_auto_commit()
    {
        // given
        using var connection = new RecordingDbConnection();
        using var transaction = new RecordingAmbientTransaction();

        // when
        var result = connection.BeginAmbientTransaction(IsolationLevel.Serializable, transaction, autoCommit: true);

        // then
        result.Should().BeSameAs(transaction);
        connection.OpenCount.Should().Be(1);
        connection.LastIsolationLevel.Should().Be(IsolationLevel.Serializable);
        transaction.AutoCommit.Should().BeTrue();
        transaction.DbTransaction.Should().BeSameAs(connection.LastTransaction);
        transaction.Current.Current.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task begin_ambient_transaction_async_should_use_async_open_and_begin_transaction_paths()
    {
        // given
        await using var connection = new RecordingDbConnection();
        await using var transaction = new RecordingAmbientTransaction();

        // when
        var result = await connection.BeginAmbientTransactionAsync(
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
        transaction.Current.Current.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task begin_ambient_transaction_async_should_propagate_open_failure_without_mutating_transaction()
    {
        // given
        var failure = new InvalidOperationException("open failed");
        await using var connection = new RecordingDbConnection { OpenAsyncException = failure };
        await using var transaction = new RecordingAmbientTransaction();

        // when
        var act = async () =>
            await connection.BeginAmbientTransactionAsync(
                IsolationLevel.Unspecified,
                transaction,
                autoCommit: true,
                CancellationToken.None
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("open failed");
        transaction.DbTransaction.Should().BeNull();
        transaction.AutoCommit.Should().BeFalse();
        transaction.Current.Current.Should().BeNull();
    }

    [Fact]
    public void setting_db_transaction_to_null_should_clear_current_when_it_points_to_same_transaction()
    {
        // given
        using var transaction = new RecordingAmbientTransaction { DbTransaction = new object() };

        // when
        transaction.DbTransaction = null;

        // then
        transaction.Current.Current.Should().BeNull();
    }

    [Fact]
    public void setting_db_transaction_to_null_should_not_clear_another_current_transaction()
    {
        // given
        using var first = new RecordingAmbientTransaction { DbTransaction = new object() };
        using var second = new RecordingAmbientTransaction(first.Current) { DbTransaction = new object() };

        // when
        first.DbTransaction = null;

        // then
        first.Current.Current.Should().BeSameAs(second);
    }

    [Fact]
    public void commit_should_drain_registered_work_once()
    {
        // given
        using var transaction = new RecordingAmbientTransaction();
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );

        // when
        transaction.Commit();
        transaction.CompleteExternally();

        // then
        transaction.CommitCount.Should().Be(1);
        drainCount.Should().Be(1);
    }

    [Fact]
    public async Task commit_async_should_pass_cancellation_to_registered_work()
    {
        // given
        await using var transaction = new RecordingAmbientTransaction();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var observedCancellation = false;
        transaction.RegisterCommitWork(
            cancellationToken =>
            {
                observedCancellation = cancellationToken.IsCancellationRequested;
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }
        );

        // when
        var act = async () => await transaction.CommitAsync(cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        observedCancellation.Should().BeTrue();
    }

    [Fact]
    public void rollback_should_discard_registered_work()
    {
        // given
        using var transaction = new RecordingAmbientTransaction();
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
        transaction.RollbackCount.Should().Be(1);
        drainCount.Should().Be(0);
    }

    [Fact]
    public void complete_externally_should_drain_when_db_transaction_is_detached_without_retouching_current()
    {
        // given
        using var transaction = new RecordingAmbientTransaction { DbTransaction = new object() };
        var drainCount = 0;
        transaction.RegisterCommitWork(
            _ =>
            {
                drainCount++;
                return ValueTask.CompletedTask;
            }
        );
        transaction.DbTransaction = null;

        // when
        transaction.CompleteExternally();

        // then
        drainCount.Should().Be(1);
        transaction.Current.Current.Should().BeNull();
        transaction.CommitCount.Should().Be(0);
    }

    [Fact]
    public void register_commit_work_should_throw_after_completion()
    {
        // given
        using var transaction = new RecordingAmbientTransaction();
        transaction.CompleteExternally();

        // when
        var act = () => transaction.RegisterCommitWork(_ => ValueTask.CompletedTask);

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void dispose_should_dispose_underlying_transaction_and_clear_current()
    {
        // given
        var dbTransaction = new DisposableTransaction();
        var transaction = new RecordingAmbientTransaction { DbTransaction = dbTransaction };

        // when
        transaction.Dispose();

        // then
        dbTransaction.DisposeCount.Should().Be(1);
        transaction.DbTransaction.Should().BeNull();
        transaction.Current.Current.Should().BeNull();
    }

    [Fact]
    public async Task dispose_async_should_dispose_underlying_transaction_and_clear_current()
    {
        // given
        var dbTransaction = new AsyncDisposableTransaction();
        var transaction = new RecordingAmbientTransaction { DbTransaction = dbTransaction };

        // when
        await transaction.DisposeAsync();

        // then
        dbTransaction.DisposeAsyncCount.Should().Be(1);
        transaction.DbTransaction.Should().BeNull();
        transaction.Current.Current.Should().BeNull();
    }

    private sealed class RecordingAmbientTransaction : AmbientTransactionBase
    {
        public RecordingAmbientTransaction()
            : this(new AsyncLocalCurrentAmbientTransaction()) { }

        public RecordingAmbientTransaction(ICurrentAmbientTransaction current)
            : base(current)
        {
            Current = current;
        }

        public ICurrentAmbientTransaction Current { get; }

        public int CommitCount { get; private set; }

        public int RollbackCount { get; private set; }

        public override void Commit()
        {
            CommitCount++;
            DrainCommitWork();
        }

        public override async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            await DrainCommitWorkAsync(cancellationToken);
        }

        public override void Rollback()
        {
            RollbackCount++;
            DiscardCommitWork();
        }

        public override Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            DiscardCommitWork();
            return Task.CompletedTask;
        }
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

        public override string ServerVersion => "1";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            OpenCount++;
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            if (OpenAsyncException is not null)
            {
                return Task.FromException(OpenAsyncException);
            }

            OpenAsyncCount++;
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            LastIsolationLevel = isolationLevel;
            LastTransaction = new RecordingDbTransaction(this, isolationLevel);
            return LastTransaction;
        }

        protected override ValueTask<DbTransaction> BeginDbTransactionAsync(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken
        )
        {
            BeginTransactionAsyncCount++;
            LastIsolationLevel = isolationLevel;
            LastTransaction = new RecordingDbTransaction(this, isolationLevel);
            return ValueTask.FromResult<DbTransaction>(LastTransaction);
        }

        protected override DbCommand CreateDbCommand()
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

    private sealed class DisposableTransaction : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class AsyncDisposableTransaction : IAsyncDisposable, IDisposable
    {
        public int DisposeAsyncCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
