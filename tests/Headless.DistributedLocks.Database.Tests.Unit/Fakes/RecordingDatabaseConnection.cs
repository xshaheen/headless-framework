// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.DistributedLocks;

namespace Tests.Fakes;

/// <summary>
/// A <see cref="DatabaseConnection"/> double whose only job is to be opened/closed/disposed by the multiplexing engine
/// and identified by the fake strategy. Lifecycle is observed through the underlying <see cref="FakeDbConnection"/>
/// (open/close counters and current state) so a test can assert share-vs-dedicate decisions — for example "only one
/// connection was opened" or "the connection stays open while it still holds locks".
/// </summary>
internal sealed class RecordingDatabaseConnection : DatabaseConnection
{
    private readonly FakeDbConnection _fake;

    private RecordingDatabaseConnection(FakeDbConnection fake, TimeProvider timeProvider)
        : base(fake, isExternallyOwned: false, timeProvider, monitoringCommandTimeoutSeconds: 5)
    {
        _fake = fake;
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Creates an internally-owned connection that starts <em>closed</em> — matching a real provider's
    /// <c>NpgsqlDataSource.CreateConnection()</c>, so the connection monitor begins in the stopped state and
    /// <c>OpenAsync</c> starts it.
    /// </summary>
    public static RecordingDatabaseConnection CreateClosed(TimeProvider timeProvider)
    {
        var fake = new FakeDbConnection();
        fake.SetState(ConnectionState.Closed);

        return new RecordingDatabaseConnection(fake, timeProvider);
    }

    /// <summary>Stable identity used by the fake strategy to track which connection holds which lock.</summary>
    public Guid Id { get; }

    public int OpenCount => _fake.OpenCount;

    public int CloseCount => _fake.CloseCount;

    public int DisposeCount => _fake.DisposeCount;

    public bool IsOpen => _fake.State == ConnectionState.Open;

    public override bool ShouldPrepareCommands => false;

    public override bool IsCommandCancellationException(Exception exception)
    {
        return exception is OperationCanceledException;
    }

    public override Task SleepAsync(
        TimeSpan sleepTime,
        Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor,
        CancellationToken cancellationToken
    )
    {
        return Task.CompletedTask;
    }
}
