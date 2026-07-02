// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// No-op <see cref="IReleaseSignal"/> for the SQL Server provider. SQL Server blocks contended acquires inside
/// <c>sp_getapplock @LockTimeout</c> (<see cref="IConnectionScopedLockStorage.BlocksServerSide"/> is <see langword="true"/>), so
/// the provider's polling/wait loop — and therefore <see cref="WaitAsync"/> — is never reached. This implementation
/// only satisfies the provider's constructor contract; it neither waits nor signals.
/// </summary>
internal sealed class NullReleaseSignal : IReleaseSignal
{
    public ValueTask WaitAsync(string resource, TimeSpan pollingFallback, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.CompletedTask;
    }
}
