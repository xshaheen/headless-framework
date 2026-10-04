// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.Fakes;

/// <summary>
/// Concrete <see cref="DatabaseConnection"/> over a <see cref="FakeDbConnection"/> for exercising
/// <c>ConnectionMonitor</c> in isolation. <see cref="SleepAsync"/> mirrors a real provider's server-side sleep by
/// routing the supplied executor through a command, so the monitoring probe is observable on the fake connection.
/// </summary>
internal sealed class TestDatabaseConnection(
    FakeDbConnection connection,
    TimeProvider timeProvider,
    int monitoringCommandTimeoutSeconds
) : DatabaseConnection(connection, isExternallyOwned: false, timeProvider, monitoringCommandTimeoutSeconds)
{
    public FakeDbConnection Fake { get; } = connection;

    public override bool ShouldPrepareCommands => false;

    public override bool IsCommandCancellationException(Exception exception)
    {
        return exception is OperationCanceledException;
    }

    public override async Task SleepAsync(
        TimeSpan sleepTime,
        Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor,
        CancellationToken cancellationToken
    )
    {
        using var command = CreateCommand();
        command.SetCommandText("SELECT 1 /* monitoring probe */");

        await executor(command, cancellationToken).ConfigureAwait(false);
    }
}
