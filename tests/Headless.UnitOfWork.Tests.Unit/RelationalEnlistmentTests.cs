// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

public sealed class RelationalEnlistmentTests : TestBase
{
    [Fact]
    public void should_return_the_connection_and_transaction_of_a_live_unit()
    {
        using var connection = new FakeConnection(ConnectionState.Open);
        using var transaction = new FakeTransaction(connection);

        var (liveConnection, liveTransaction) = _RequireLive(_Unit(connection, transaction));

        liveConnection.Should().BeSameAs(connection);
        liveTransaction.Should().BeSameAs(transaction);
    }

    [Fact]
    public void should_refuse_a_transaction_of_another_provider()
    {
        using var connection = new FakeConnection(ConnectionState.Open);
        using var transaction = new FakeTransaction(connection);
        var act = () =>
            RelationalEnlistment.RequireLive(
                _Unit(connection, transaction),
                typeof(FakeConnection),
                typeof(OtherTransaction),
                "Headless.Test",
                "test"
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Headless.Test runs enlisted test calls only through*");
    }

    [Fact]
    public void should_refuse_a_connection_of_another_provider()
    {
        using var connection = new FakeConnection(ConnectionState.Open);
        using var transaction = new FakeTransaction(connection);
        var act = () =>
            RelationalEnlistment.RequireLive(
                _Unit(connection, transaction),
                typeof(OtherConnection),
                typeof(FakeTransaction),
                "Headless.Test",
                "test"
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an open OtherConnection*");
    }

    [Fact]
    public void should_refuse_a_closed_connection()
    {
        using var connection = new FakeConnection(ConnectionState.Closed);
        using var transaction = new FakeTransaction(connection);

        var act = () => _RequireLive(_Unit(connection, transaction));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not an open FakeConnection*");
    }

    [Fact]
    public void should_refuse_a_transaction_bound_to_another_connection()
    {
        // A finished transaction drops its connection, which reads the same as being bound to another one.
        using var connection = new FakeConnection(ConnectionState.Open);
        using var transaction = new FakeTransaction(connection: null);

        var act = () => _RequireLive(_Unit(connection, transaction));

        act.Should().Throw<InvalidOperationException>().WithMessage("*not bound to its connection*");
    }

    [Fact]
    public void should_refuse_a_unit_that_is_not_active()
    {
        using var connection = new FakeConnection(ConnectionState.Open);
        using var transaction = new FakeTransaction(connection);
        var unit = _Unit(connection, transaction);
        unit.State.Returns(UnitOfWorkState.Completed);

        var act = () => _RequireLive(unit);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_accept_a_unit_on_the_configured_database()
    {
        using var configured = new FakeConnection(ConnectionState.Closed, "localhost", "leases");
        using var candidate = new FakeConnection(ConnectionState.Open, "127.0.0.1", "leases");
        var act = () => RelationalEnlistment.RequireSameDatabase(configured, candidate, "Headless.Test", "test");

        act.Should().NotThrow();
    }

    [Fact]
    public void should_refuse_a_unit_on_another_database_and_name_both()
    {
        using var configured = new FakeConnection(ConnectionState.Closed, "localhost", "leases");
        using var candidate = new FakeConnection(ConnectionState.Open, "localhost", "orders");
        var act = () => RelationalEnlistment.RequireSameDatabase(configured, candidate, "Headless.Test", "test");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*database 'orders'*Headless.Test is configured for database 'leases'*");
    }

    private static (DbConnection, DbTransaction) _RequireLive(IUnitOfWork unit)
    {
        return RelationalEnlistment.RequireLive(
            unit,
            typeof(FakeConnection),
            typeof(FakeTransaction),
            "Headless.Test",
            "test"
        );
    }

    private static IUnitOfWork _Unit(DbConnection connection, DbTransaction transaction)
    {
        var resource = Substitute.For<IRelationalUnitOfWorkResource>();
        resource.IsTransactionCompleted.Returns(false);
        resource.Connection.Returns(connection);
        resource.Transaction.Returns(transaction);
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(resource);

        return unit;
    }

    private class FakeConnection(ConnectionState state, string dataSource = "localhost", string database = "leases")
        : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database { get; } = database;

        public override string DataSource { get; } = dataSource;

        public override string ServerVersion => "0";

        public override ConnectionState State { get; } = state;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close() { }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class OtherConnection() : FakeConnection(ConnectionState.Open);

    private class FakeTransaction(DbConnection? connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection { get; } = connection;

        public override void Commit() { }

        public override void Rollback() { }
    }

    private sealed class OtherTransaction() : FakeTransaction(connection: null);
}
