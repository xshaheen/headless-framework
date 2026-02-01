// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Sql;
using NSubstitute;

namespace Tests;

/// <summary>
/// Unit tests for <see cref="DefaultSqlCurrentConnection"/>.
/// </summary>
public sealed class DefaultSqlCurrentConnectionTests
{
    [Fact]
    public async Task should_create_connection_on_first_call()
    {
        // given
        var ct = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        await using var sut = new DefaultSqlCurrentConnection(factory);

        // when
        var result = await sut.GetOpenConnectionAsync(ct);

        // then
        result.Should().BeSameAs(connection);
        await factory.Received(1).CreateNewConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_reuse_existing_open_connection()
    {
        // given
        var ct = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        await using var sut = new DefaultSqlCurrentConnection(factory);

        // when
        var first = await sut.GetOpenConnectionAsync(ct);
        var second = await sut.GetOpenConnectionAsync(ct);

        // then
        first.Should().BeSameAs(second);
        await factory.Received(1).CreateNewConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_create_new_connection_when_existing_is_closed()
    {
        // given
        var ct = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var closedConnection = Substitute.For<DbConnection>();
        closedConnection.State.Returns(ConnectionState.Closed);

        var openConnection = Substitute.For<DbConnection>();
        openConnection.State.Returns(ConnectionState.Open);

        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(closedConnection, openConnection);

        await using var sut = new DefaultSqlCurrentConnection(factory);

        // when
        _ = await sut.GetOpenConnectionAsync(ct); // Gets closedConnection
        var result = await sut.GetOpenConnectionAsync(ct); // Should create new connection

        // then
        result.Should().BeSameAs(openConnection);
        await factory.Received(2).CreateNewConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_dispose_connection_on_dispose_async()
    {
        // given
        var ct = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        var sut = new DefaultSqlCurrentConnection(factory);
        _ = await sut.GetOpenConnectionAsync(ct);

        // when
        await sut.DisposeAsync();

        // then
        await connection.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task should_not_dispose_when_no_connection_created()
    {
        // given
        var factory = Substitute.For<ISqlConnectionFactory>();
        var sut = new DefaultSqlCurrentConnection(factory);

        // when
        await sut.DisposeAsync();

        // then - no exception thrown, factory never called
        await factory.DidNotReceive().CreateNewConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_cancellation_token_to_factory()
    {
        // given
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        using var cts = new CancellationTokenSource();
        await using var sut = new DefaultSqlCurrentConnection(factory);

        // when
        _ = await sut.GetOpenConnectionAsync(cts.Token);

        // then
        await factory.Received(1).CreateNewConnectionAsync(cts.Token);
    }

    [Fact]
    public async Task should_dispose_existing_before_creating_new()
    {
        // given
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var closedConnection = new FakeDbConnection { StateValue = ConnectionState.Closed };

        var openConnection = Substitute.For<DbConnection>();
        openConnection.State.Returns(ConnectionState.Open);

        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(closedConnection, openConnection);

        await using var sut = new DefaultSqlCurrentConnection(factory);

        // when
        _ = await sut.GetOpenConnectionAsync(cancellationToken); // Gets closedConnection
        _ = await sut.GetOpenConnectionAsync(cancellationToken); // Should dispose closed and create new

        // then
        closedConnection.DisposeCallCount.Should().Be(1);
    }

    [Fact]
    public async Task should_handle_concurrent_access_with_async_lock()
    {
        // given
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        await using var sut = new DefaultSqlCurrentConnection(factory);

        // when - concurrent access
        var tasks = Enumerable
            .Range(0, 10)
            .Select(_ => sut.GetOpenConnectionAsync(cancellationToken).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // then - all return same connection, factory called only once
        results.Should().AllSatisfy(x => x.Should().BeSameAs(connection));
        await factory.Received(1).CreateNewConnectionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_set_connection_to_null_on_dispose()
    {
        // given
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        var sut = new DefaultSqlCurrentConnection(factory);
        _ = await sut.GetOpenConnectionAsync(cancellationToken);

        // when
        await sut.DisposeAsync();

        // then - subsequent GetOpenConnectionAsync should create new connection
        var newConnection = Substitute.For<DbConnection>();
        newConnection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(newConnection);

        var result = await sut.GetOpenConnectionAsync(cancellationToken);
        result.Should().BeSameAs(newConnection);
    }

    [Fact]
    public async Task should_handle_dispose_when_already_closed()
    {
        // given
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        var sut = new DefaultSqlCurrentConnection(factory);
        _ = await sut.GetOpenConnectionAsync(cancellationToken);

        // Change state to closed before dispose
        connection.State.Returns(ConnectionState.Closed);

        // when/then - should not throw
        Func<Task> act = async () => await sut.DisposeAsync();
        await act.Should().NotThrowAsync();
        await connection.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task should_not_throw_on_multiple_disposes()
    {
        // given
        var cancellationToken = TestContext.Current.CancellationToken;
        var factory = Substitute.For<ISqlConnectionFactory>();
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Open);
        factory.CreateNewConnectionAsync(Arg.Any<CancellationToken>()).Returns(connection);

        var sut = new DefaultSqlCurrentConnection(factory);
        _ = await sut.GetOpenConnectionAsync(cancellationToken);

        // when - multiple disposes
        await sut.DisposeAsync();

        // Connection state is now effectively null after first dispose
        connection.State.Returns(ConnectionState.Closed);

        Func<Task> act = async () => await sut.DisposeAsync();

        // then - should not throw
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Fake DbConnection to track Dispose calls (NSubstitute cannot intercept non-virtual Dispose).
    /// </summary>
    private sealed class FakeDbConnection : DbConnection
    {
        public ConnectionState StateValue { get; set; } = ConnectionState.Closed;
        public int DisposeCallCount { get; private set; }

#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member
        public override string ConnectionString { get; set; } = string.Empty;
#pragma warning restore CS8765
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override ConnectionState State => StateValue;

        public override void ChangeDatabase(string databaseName) { }

        public override void Close() { }

        public override void Open() { }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => null!;

        protected override DbCommand CreateDbCommand() => null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCallCount++;
            }
            base.Dispose(disposing);
        }
    }
}
