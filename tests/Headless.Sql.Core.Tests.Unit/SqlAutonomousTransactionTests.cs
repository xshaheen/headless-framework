// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlAutonomousTransactionTests : TestBase
{
    private static readonly ISqlDialect _Dialect = _ClassifyingDialect();

    [Fact]
    public async Task should_commit_the_attempt_and_return_its_result()
    {
        var connections = new List<FakeConnection>();

        var result = await SqlAutonomousTransaction.RunAsync(
            _Dialect,
            () => _Track(connections),
            static (_, _, _) => Task.FromResult(42),
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(42);
        connections.Should().ContainSingle();
        connections[0].Transactions.Should().ContainSingle().Which.Committed.Should().BeTrue();
        connections[0].Transactions[0].IsolationLevel.Should().Be(IsolationLevel.ReadCommitted);
        connections[0].WasDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(SqlErrorKind.Deadlock)]
    [InlineData(SqlErrorKind.SerializationConflict)]
    public async Task should_retry_a_transient_conflict_on_a_new_connection(SqlErrorKind kind)
    {
        var connections = new List<FakeConnection>();
        var attempts = 0;

        var result = await SqlAutonomousTransaction.RunAsync(
            _Dialect,
            () => _Track(connections),
            (_, _, _) => ++attempts == 1 ? throw new ClassifiedException(kind) : Task.FromResult(attempts),
            TimeProvider.System,
            AbortToken
        );

        result.Should().Be(2);
        connections.Should().HaveCount(2, "a victim's transaction is gone, so the retry starts on a fresh connection");
        connections[0].Transactions[0].Committed.Should().BeFalse();
        connections[1].Transactions[0].Committed.Should().BeTrue();
    }

    [Theory]
    [InlineData(SqlErrorKind.None)]
    [InlineData(SqlErrorKind.UniqueViolation)]
    [InlineData(SqlErrorKind.LockTimeout)]
    public async Task should_not_retry_any_other_failure(SqlErrorKind kind)
    {
        var connections = new List<FakeConnection>();

        var act = async () =>
            await SqlAutonomousTransaction.RunAsync<int>(
                _Dialect,
                () => _Track(connections),
                (_, _, _) => throw new ClassifiedException(kind),
                TimeProvider.System,
                AbortToken
            );

        await act.Should().ThrowAsync<ClassifiedException>();
        connections.Should().ContainSingle();
    }

    [Fact]
    public async Task should_give_up_after_the_last_attempt_and_surface_the_conflict()
    {
        var connections = new List<FakeConnection>();

        var act = async () =>
            await SqlAutonomousTransaction.RunAsync<int>(
                _Dialect,
                () => _Track(connections),
                (_, _, _) => throw new ClassifiedException(SqlErrorKind.Deadlock),
                TimeProvider.System,
                AbortToken
            );

        await act.Should().ThrowAsync<ClassifiedException>();
        connections.Should().HaveCount(3);
    }

    [Fact]
    public async Task should_commit_even_when_cancelled_after_the_statements_decided()
    {
        using var cancellation = new CancellationTokenSource();
        var connections = new List<FakeConnection>();

        var result = await SqlAutonomousTransaction.RunAsync(
            _Dialect,
            () => _Track(connections),
            async (_, _, _) =>
            {
                // The write has been decided; the caller is about to be told it happened.
                await cancellation.CancelAsync();

                return 1;
            },
            TimeProvider.System,
            cancellation.Token
        );

        result.Should().Be(1);
        connections[0].Transactions[0].Committed.Should().BeTrue();
    }

    private static FakeConnection _Track(List<FakeConnection> connections)
    {
        var connection = new FakeConnection();
        connections.Add(connection);

        return connection;
    }

    private static ISqlDialect _ClassifyingDialect()
    {
        var dialect = Substitute.For<ISqlDialect>();
        dialect
            .Classify(Arg.Any<Exception>())
            .Returns(call => call.Arg<Exception>() is ClassifiedException e ? e.Kind : SqlErrorKind.None);

        return dialect;
    }

    private sealed class ClassifiedException(SqlErrorKind kind) : Exception(kind.ToString())
    {
        public SqlErrorKind Kind { get; } = kind;
    }

    private sealed class FakeConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public List<FakeTransaction> Transactions { get; } = [];

        public bool WasDisposed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "db";

        public override string DataSource => "localhost";

        public override string ServerVersion => "0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

        public override ValueTask DisposeAsync()
        {
            WasDisposed = true;

            return base.DisposeAsync();
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            var transaction = new FakeTransaction(this, isolationLevel);
            Transactions.Add(transaction);

            return transaction;
        }

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class FakeTransaction(DbConnection connection, IsolationLevel isolationLevel) : DbTransaction
    {
        public bool Committed { get; private set; }

        public override IsolationLevel IsolationLevel { get; } = isolationLevel;

        protected override DbConnection DbConnection { get; } = connection;

        public override void Commit() => Committed = true;

        public override void Rollback() { }
    }
}
