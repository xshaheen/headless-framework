// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Tests;

/// <summary>A connection whose transactions record their commits, and faults the transient classifier knows.</summary>
internal static class SqlAutonomousFakes
{
    public static FakeDbException Transient(string fault)
    {
        // "driver-flag" is a fault the driver marks transient; anything else is a SQLSTATE the classifier knows.
        return string.Equals(fault, "driver-flag", StringComparison.Ordinal)
            ? new FakeDbException(isTransient: true)
            : new FakeDbException(isTransient: false, sqlState: fault);
    }

    public static FakeConnection Track(
        List<FakeConnection> connections,
        Exception? beginFault = null,
        Exception? commitFault = null
    )
    {
        var connection = new FakeConnection(beginFault, commitFault);
        connections.Add(connection);

        return connection;
    }
}

internal sealed class FakeDbException(bool isTransient, string? sqlState = null) : DbException("database fault")
{
    public override bool IsTransient { get; } = isTransient;

    public override string? SqlState { get; } = sqlState;
}

internal sealed class FakeConnection(Exception? beginFault, Exception? commitFault) : DbConnection
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
        if (beginFault is not null)
        {
            throw beginFault;
        }

        var transaction = new FakeTransaction(this, isolationLevel, commitFault);
        Transactions.Add(transaction);

        return transaction;
    }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
}

internal sealed class FakeTransaction(DbConnection connection, IsolationLevel isolationLevel, Exception? commitFault)
    : DbTransaction
{
    public bool Committed { get; private set; }

    public override IsolationLevel IsolationLevel { get; } = isolationLevel;

    protected override DbConnection DbConnection { get; } = connection;

    public override void Commit()
    {
        if (commitFault is not null)
        {
            throw commitFault;
        }

        Committed = true;
    }

    public override void Rollback() { }
}
