// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

namespace Tests.Fakes;

/// <summary>
/// A test double for <see cref="DbConnection"/> that lets a test drive <see cref="State"/> transitions (raising
/// <see cref="DbConnection.StateChange"/>) and observe the keepalive/monitoring queries issued against it. Execution is routed
/// through <see cref="ExecuteNonQueryHandler"/> so a test can count probes or simulate a stalled connection.
/// </summary>
internal sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Open;
    private int _executeNonQueryCount;

    /// <summary>Number of <c>ExecuteNonQueryAsync</c> calls observed (keepalive + monitoring probes).</summary>
    public int ExecuteNonQueryCount => Volatile.Read(ref _executeNonQueryCount);

    /// <summary>Number of <c>OpenAsync</c> calls observed.</summary>
    public int OpenCount { get; private set; }

    /// <summary>Number of <c>Close</c> calls observed.</summary>
    public int CloseCount { get; private set; }

    /// <summary>Number of <c>DisposeAsync</c> calls observed.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// Optional hook invoked for every <c>ExecuteNonQueryAsync</c>. Defaults to an immediate success (returns 0).
    /// A test can override it to block (simulate a stalled connection) or to signal when a probe ran.
    /// </summary>
    public Func<FakeDbCommand, CancellationToken, Task<int>>? ExecuteNonQueryHandler { get; set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = "fake";

    public override string Database => "fake";

    public override string DataSource => "fake";

    public override string ServerVersion => "0.0";

    public override ConnectionState State => _state;

    /// <summary>Transitions the connection to <paramref name="newState"/> and raises <see cref="DbConnection.StateChange"/>.</summary>
    public void SetState(ConnectionState newState)
    {
        var original = _state;

        if (original == newState)
        {
            return;
        }

        _state = newState;
        OnStateChange(new StateChangeEventArgs(original, newState));
    }

    internal async Task<int> OnExecuteNonQueryAsync(FakeDbCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executeNonQueryCount);

        var handler = ExecuteNonQueryHandler;

        return handler is not null ? await handler(command, cancellationToken).ConfigureAwait(false) : 0;
    }

    public override void Open()
    {
        OpenCount++;
        _state = ConnectionState.Open;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        OpenCount++;
        _state = ConnectionState.Open;

        return Task.CompletedTask;
    }

    public override void Close()
    {
        CloseCount++;
        SetState(ConnectionState.Closed);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCount++;
        }

        base.Dispose(disposing);
    }

    public override void ChangeDatabase(string databaseName) { }

    protected override DbCommand CreateDbCommand()
    {
        return new FakeDbCommand(this);
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotSupportedException("Transactions are not exercised by these tests.");
    }
}
