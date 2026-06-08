// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.AmbientTransactions;

namespace Tests;

public abstract class RecordingDbAmbientTransactionFixture : AmbientTransactionFixtureBase
{
    private readonly AsyncLocalCurrentAmbientTransaction _current = new();

    public override ICurrentAmbientTransaction CurrentAmbientTransaction => _current;

    public RecordingDbConnection? LastConnection { get; private set; }

    public override ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        _current.Current = null;
        LastConnection?.Dispose();
        LastConnection = null;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<IAmbientTransaction> BeginTransactionAsync(
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        return BeginTransactionAsync(IsolationLevel.Unspecified, autoCommit, cancellationToken);
    }

    public override async ValueTask<IAmbientTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        bool autoCommit = false,
        CancellationToken cancellationToken = default
    )
    {
        LastConnection = new RecordingDbConnection();
        var transaction = CreateTransaction(_current);

        return await LastConnection
            .BeginAmbientTransactionAsync(isolationLevel, transaction, autoCommit, cancellationToken)
            .ConfigureAwait(false);
    }

    protected abstract IAmbientTransaction CreateTransaction(ICurrentAmbientTransaction current);
}

public sealed class RecordingDbConnection : DbConnection
{
    private string _connectionString = "Host=localhost;Database=test";
    private ConnectionState _state = ConnectionState.Closed;

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
        _state = ConnectionState.Open;
    }

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
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
        LastIsolationLevel = isolationLevel;
        LastTransaction = new RecordingDbTransaction(this, isolationLevel);
        return ValueTask.FromResult<DbTransaction>(LastTransaction);
    }

    protected override DbCommand CreateDbCommand()
    {
        throw new NotSupportedException();
    }
}

public sealed class RecordingDbTransaction(RecordingDbConnection connection, IsolationLevel isolationLevel)
    : DbTransaction
{
    public int CommitCount { get; private set; }

    public int RollbackCount { get; private set; }

    public int CommitAsyncCount { get; private set; }

    public int RollbackAsyncCount { get; private set; }

    public override IsolationLevel IsolationLevel { get; } = isolationLevel;

    protected override DbConnection DbConnection { get; } = connection;

    public override void Commit()
    {
        CommitCount++;
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        CommitAsyncCount++;
        return Task.CompletedTask;
    }

    public override void Rollback()
    {
        RollbackCount++;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RollbackAsyncCount++;
        return Task.CompletedTask;
    }
}
