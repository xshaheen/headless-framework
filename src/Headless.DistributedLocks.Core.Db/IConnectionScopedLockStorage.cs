// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public interface IConnectionScopedLockStorage
{
    ValueTask<ConnectionScopedLockHandle?> TryAcquireAsync(
        string resource,
        string lockId,
        bool isShared,
        CancellationToken cancellationToken = default
    );

    ValueTask ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken = default);

    ValueTask ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    ValueTask<bool> IsLockedAsync(string resource, bool? isShared = null, CancellationToken cancellationToken = default);

    ValueTask<string?> GetLocalLockIdAsync(string resource, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default);

    ValueTask<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default);
}

public sealed class ConnectionScopedLockHandle : IAsyncDisposable
{
    private readonly Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> _release;
    private int _released;

    public ConnectionScopedLockHandle(
        string resource,
        string lockId,
        Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> release,
        CancellationToken connectionLostToken
    )
    {
        Resource = resource;
        LockId = lockId;
        ConnectionLostToken = connectionLostToken;
        _release = release;
    }

    public string Resource { get; }

    public string LockId { get; }

    public CancellationToken ConnectionLostToken { get; }

    public async ValueTask ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        await _release(this, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
