// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Public-facing handle for a connection-scoped advisory lock. The lock is released only when this handle is disposed
/// (or <see cref="ReleaseAsync"/> is called) — there is no TTL and no GC finalizer safety net: the storage holds a
/// strong reference to the underlying engine handle for its lifetime, so a consumer that abandons this handle without
/// disposing it leaks the backing connection and its advisory lock until the provider itself is disposed. This is the
/// deliberate contract for connection-scoped locks (mirroring <c>lock</c>/<c>using</c> discipline); the reference
/// engine's finalizer queue was intentionally dropped in favor of requiring explicit disposal. Always dispose the
/// handle (an <c>await using</c> is the intended usage).
/// </summary>
internal sealed class ConnectionScopedDistributedLockHandle : IDistributedLease
{
    private readonly ConnectionScopedLockHandle _handle;
    private readonly Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> _release;
    private readonly bool _releaseOnDispose;
    private readonly AsyncLock _gate = new();
    private int _released;
    private int _disposed;

    /// <summary>
    /// Initializes a new handle for an acquired connection-scoped lock.
    /// </summary>
    /// <param name="handle">The underlying storage handle returned by <see cref="IConnectionScopedLockStorage"/>.</param>
    /// <param name="fencingToken">Optional monotonic fencing token issued for this exclusive acquisition; <see langword="null"/> for shared locks or when no <see cref="IFencingTokenSource"/> is registered.</param>
    /// <param name="timeWaitedForLock">How long the acquire loop waited before succeeding.</param>
    /// <param name="releaseOnDispose">When <see langword="true"/>, <see cref="DisposeAsync"/> calls <see cref="ReleaseAsync"/> automatically.</param>
    /// <param name="timeProvider">Clock used to stamp <see cref="DateAcquired"/>.</param>
    /// <param name="release">Callback invoked by <see cref="ReleaseAsync"/> to release the lock in the backing store.</param>
    /// <param name="logger">Logger used to emit release-failure diagnostics.</param>
    public ConnectionScopedDistributedLockHandle(
        ConnectionScopedLockHandle handle,
        long? fencingToken,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        TimeProvider timeProvider,
        Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> release,
        ILogger logger
    )
    {
        _handle = handle;
        _release = release;
        _releaseOnDispose = releaseOnDispose;
        FencingToken = fencingToken;
        TimeWaitedForLock = timeWaitedForLock;
        DateAcquired = timeProvider.GetUtcNow();
        Logger = logger;
    }

    private ILogger Logger { get; }

    /// <inheritdoc/>
    public string LeaseId => _handle.LeaseId;

    /// <inheritdoc/>
    public long? FencingToken { get; }

    /// <inheritdoc/>
    public string Resource => _handle.Resource;

    /// <summary>
    /// Always zero for connection-scoped locks because <see cref="RenewAsync"/> is a no-op (the advisory
    /// lock is held for the connection's lifetime and has no TTL to extend).
    /// </summary>
    public int RenewalCount { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset DateAcquired { get; }

    /// <inheritdoc/>
    public TimeSpan TimeWaitedForLock { get; }

    /// <summary>
    /// Cancelled when the underlying connection is observed to be lost, signalling that the advisory lock
    /// is no longer held. Returns <see cref="CancellationToken.None"/> when acquire-time monitoring was not enabled.
    /// </summary>
    public CancellationToken LostToken => _handle.ConnectionLostToken;

    /// <summary>
    /// <see langword="true"/> when the handle was acquired with connection monitoring enabled
    /// and <see cref="LostToken"/> carries a live cancellable signal.
    /// </summary>
    public bool CanObserveLoss => _handle.ConnectionLostToken.CanBeCanceled;

    /// <summary>
    /// Releases the lock. Idempotent — only the first call invokes the release callback; subsequent calls
    /// return immediately. Thread-safe: concurrent callers are serialized by an internal <c>AsyncLock</c>.
    /// </summary>
    public async Task ReleaseAsync()
    {
        if (Volatile.Read(ref _released) != 0)
        {
            return;
        }

        using (await _gate.LockAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _released) != 0)
            {
                return;
            }

            try
            {
                await _release(_handle, CancellationToken.None).ConfigureAwait(false);
                Volatile.Write(ref _released, 1);
            }
            catch (Exception exception)
            {
                Logger.LogConnectionScopedLockReleaseFailed(exception, Resource, LeaseId);
                throw;
            }
        }
    }

    /// <summary>
    /// No-op for connection-scoped locks: the advisory lock is held for the connection's lifetime and has no
    /// TTL to extend. <see cref="RenewalCount"/> stays at zero so monitoring sees no renewal activity.
    /// Always returns <see langword="true"/>.
    /// </summary>
    /// <param name="timeUntilExpires">Ignored.</param>
    /// <param name="cancellationToken">Token observed before returning.</param>
    /// <returns><see langword="true"/> unconditionally.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        // No-op for session-scoped locks: the advisory lock is held for the connection's lifetime,
        // so there is no lease to extend. RenewalCount stays at 0 to avoid signalling monitoring
        // that the lifetime was extended.
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(true);
    }

    /// <summary>
    /// Disposes this handle. When <c>releaseOnDispose</c> was set at construction, calls <see cref="ReleaseAsync"/>.
    /// Idempotent — only the first call acts; subsequent calls return immediately.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_releaseOnDispose)
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
    }
}
