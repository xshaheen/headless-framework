// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// TUS file lock backed by <c>IDistributedLock</c>, enabling cross-process and cross-node PATCH
/// safety for TUS uploads.
/// </summary>
/// <remarks>
/// <para>
/// Each instance manages the lock lifecycle for one TUS file identified by
/// <paramref name="fileId"/>. The underlying lock resource key is
/// <c>{resourcePrefix}-{fileId}</c> (prefix default <c>tus-file-lock</c>) to avoid collisions
/// with other application-level distributed locks; give each TUS endpoint its own prefix when
/// several endpoints (different stores/containers) share one <c>IDistributedLock</c> backend and
/// may produce the same file ids. The lock is acquired with a zero-wait timeout (fail fast, no
/// blocking), and with a finite, auto-extending lease that is held while the upload runs but
/// frees the file if the holder crashes.
/// </para>
/// <para>
/// The lease provides best-effort cross-node mutual exclusion, not fenced correctness: tusdotnet's
/// <c>ITusFileLock</c> contract has no hook to observe a lease lost mid-request, so a holder that
/// loses its lease (for example, during a backend partition) keeps writing until the request ends.
/// Auto-extension shrinks that window; it cannot eliminate it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DistributedLockTusFileLock(
    string fileId,
    IDistributedLock distributedLockProvider,
    string resourcePrefix = DistributedLockTusFileLock.DefaultResourcePrefix
) : ITusFileLock, IAsyncDisposable
{
    /// <summary>The default distributed-lock resource-key prefix.</summary>
    public const string DefaultResourcePrefix = "tus-file-lock";

    private readonly string _resource = $"{resourcePrefix}-{fileId}";
    private IDistributedLease? _distributedLock;

    /// <summary>
    /// Attempts to acquire the distributed lock for this TUS file without waiting.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if another
    /// process or node already holds it
    /// </returns>
    public async Task<bool> Lock()
    {
        // Re-entrancy guard (parity with tusdotnet's InMemoryFileLock): a second Lock() on the
        // same instance must not overwrite — and thereby leak — the already-held lease.
        if (_distributedLock is not null)
        {
            return true;
        }

        _distributedLock = await distributedLockProvider
            .TryAcquireAsync(
                _resource,
                new DistributedLockAcquireOptions
                {
                    // Finite lease (provider default TTL) that auto-extends while held: a long upload keeps the lock,
                    // but a crashed holder's lease expires and frees the file. An infinite lease would stay stuck
                    // forever after a crash. AutoExtend requires a finite TTL, so TimeUntilExpires is left at default.
                    AcquireTimeout = TimeSpan.Zero, // try once, no wait — a concurrent PATCH on the same file fails fast
                    Monitoring = LockMonitoringMode.AutoExtend,
                }
            )
            .ConfigureAwait(false);

        return _distributedLock is not null;
    }

    /// <summary>
    /// Releases the distributed lock if this instance holds it; otherwise does nothing.
    /// </summary>
    /// <returns>a completed task when no lock is held; otherwise the release task</returns>
    public async Task ReleaseIfHeld()
    {
        if (_distributedLock is not null)
        {
            // Disposing releases the lease (ReleaseOnDispose defaults true) and stops the auto-extend monitor.
            await _distributedLock.DisposeAsync();
            _distributedLock = null;
        }
    }

    // tusdotnet calls ReleaseIfHeld in its finally, but implement IAsyncDisposable as a safety net so the
    // distributed lease is still released if a caller disposes the lock instead of calling ReleaseIfHeld.
    public ValueTask DisposeAsync()
    {
        return new ValueTask(ReleaseIfHeld());
    }
}
