// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.DistributedLocks;

/// <summary>
/// One unit-of-work handle bound to the transaction-lock feature, returned by <c>unit.TransactionLocks</c>. A lock
/// taken through it lives inside that unit's transaction and is released when the unit completes or rolls back.
/// </summary>
/// <remarks>
/// <para>
/// One binding per unit, created on the first read of <c>unit.TransactionLocks</c> and kept as unit-local state;
/// it owns nothing to dispose. The handle's liveness is checked when a lock runs, so a binding retained past the
/// unit's completion throws on its next call.
/// </para>
/// <para>
/// The <c>AcquireAll</c> forms take several resources in one call. The set is deduplicated and acquired in ordinal
/// order, so two callers locking overlapping sets cannot deadlock on each other however each listed them; one wait
/// budget covers the whole set; and the set is all-or-nothing, so a failed call holds none of the locks it took.
/// Pass the complete set in one call: two calls in sequence are two sets, and their order is the caller's.
/// </para>
/// <para>
/// A <c>TryAcquire</c> form answers contention with <see langword="null" />, the shape for a bounded wait that
/// refuses the request (for example 500 ms, then a 409). An <c>Acquire</c> form throws
/// <see cref="LockAcquisitionTimeoutException" />, the shape for a wait whose expiry fails the operation (for example
/// 5 seconds, then a 503 with <c>Retry-After</c>). The synchronous forms serve callers that cannot await, such as an
/// EF Core <c>SavingChanges</c> interceptor, and block the calling thread for up to the wait.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkTransactionLocks
{
    // One default on every provider, so a caller moving between engines keeps one wait budget; it matches the
    // static helpers and the session-scoped providers.
    private static readonly TimeSpan _DefaultAcquireTimeout = TimeSpan.FromSeconds(30);

    private readonly IUnitOfWorkTransactionLocks _locks;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkTransactionLocks(IUnitOfWorkTransactionLocks locks, IUnitOfWork unitOfWork)
    {
        _locks = locks;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on <paramref name="resource" /> inside the bound unit's
    /// transaction, waiting up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resource">The logical resource name, such as one built by <see cref="LockKey" />.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> waits up to 30 seconds;
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits without bound; <see cref="TimeSpan.Zero" /> is one attempt.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The held lock.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource" /> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">The lock was still held by another session when the wait elapsed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> fired before the lock was held.</exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public async ValueTask<TransactionLockHandle> AcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resources = _Single(resource);
        var timeout = _ResolveTimeout(acquireTimeout, _DefaultAcquireTimeout);

        return await _locks.TryAcquireAsync(_unitOfWork, resources, timeout, cancellationToken).ConfigureAwait(false)
            ? new TransactionLockHandle(resource)
            : throw _TimedOut(resources, timeout);
    }

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on <paramref name="resource" /> inside the bound
    /// unit's transaction, waiting up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resource">The logical resource name, such as one built by <see cref="LockKey" />.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> and <see cref="TimeSpan.Zero" /> are
    /// one attempt; <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database command.</param>
    /// <returns>The held lock, or <see langword="null" /> when another session still held it as the wait elapsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource" /> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> fired before the lock was held.</exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public async ValueTask<TransactionLockHandle?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var resources = _Single(resource);
        var timeout = _ResolveTimeout(acquireTimeout, TimeSpan.Zero);

        return await _locks.TryAcquireAsync(_unitOfWork, resources, timeout, cancellationToken).ConfigureAwait(false)
            ? new TransactionLockHandle(resource)
            : null;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on every distinct resource in <paramref name="resources" />,
    /// in ordinal order and under one wait budget, inside the bound unit's transaction.
    /// </summary>
    /// <param name="resources">
    /// The logical resource names. The sequence is enumerated once, deduplicated, and ordinal-sorted.
    /// </param>
    /// <param name="acquireTimeout">
    /// The wait budget for the whole set. <see langword="null" /> waits up to 30 seconds;
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits without bound; <see cref="TimeSpan.Zero" /> is one attempt per
    /// resource.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>One held lock per distinct resource, in acquisition (ordinal) order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resources" /> is empty or contains a <see langword="null" />, empty, or whitespace name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">
    /// One of the resources was still held by another session when the budget ran out; none of the set's new locks
    /// remain held. For a set of two or more, <see cref="LockAcquisitionTimeoutException.Resource" /> carries the
    /// joined canonical set (for example <c>"a+b"</c>), not the resource that blocked; it is diagnostic only.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> fired before the set was held.</exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public async ValueTask<IReadOnlyList<TransactionLockHandle>> AcquireAllAsync(
        IEnumerable<string> resources,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var canonical = _Canonicalize(resources);
        var timeout = _ResolveTimeout(acquireTimeout, _DefaultAcquireTimeout);

        return await _locks.TryAcquireAsync(_unitOfWork, canonical, timeout, cancellationToken).ConfigureAwait(false)
            ? _Handles(canonical)
            : throw _TimedOut(canonical, timeout);
    }

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on every distinct resource in
    /// <paramref name="resources" />, in ordinal order and under one wait budget, inside the bound unit's transaction.
    /// </summary>
    /// <param name="resources">
    /// The logical resource names. The sequence is enumerated once, deduplicated, and ordinal-sorted.
    /// </param>
    /// <param name="acquireTimeout">
    /// The wait budget for the whole set. <see langword="null" /> and <see cref="TimeSpan.Zero" /> are one attempt per
    /// resource; <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the database commands.</param>
    /// <returns>
    /// One held lock per distinct resource, in acquisition (ordinal) order; or <see langword="null" /> when another
    /// session still held one of them as the budget ran out, in which case none of the set's new locks remain held.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resources" /> is empty or contains a <see langword="null" />, empty, or whitespace name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> fired before the set was held.</exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public async ValueTask<IReadOnlyList<TransactionLockHandle>?> TryAcquireAllAsync(
        IEnumerable<string> resources,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var canonical = _Canonicalize(resources);
        var timeout = _ResolveTimeout(acquireTimeout, TimeSpan.Zero);

        return await _locks.TryAcquireAsync(_unitOfWork, canonical, timeout, cancellationToken).ConfigureAwait(false)
            ? _Handles(canonical)
            : null;
    }

    /// <summary>
    /// Synchronous form of <see cref="AcquireAsync" /> for callers that cannot await, such as an EF Core
    /// <c>SavingChanges</c> interceptor. Blocks the calling thread for up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resource">The logical resource name, such as one built by <see cref="LockKey" />.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> waits up to 30 seconds;
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits without bound; <see cref="TimeSpan.Zero" /> is one attempt.
    /// </param>
    /// <returns>The held lock.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource" /> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">The lock was still held by another session when the wait elapsed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public TransactionLockHandle Acquire(string resource, TimeSpan? acquireTimeout = null)
    {
        var resources = _Single(resource);
        var timeout = _ResolveTimeout(acquireTimeout, _DefaultAcquireTimeout);

        return _locks.TryAcquire(_unitOfWork, resources, timeout)
            ? new TransactionLockHandle(resource)
            : throw _TimedOut(resources, timeout);
    }

    /// <summary>
    /// Synchronous form of <see cref="TryAcquireAsync" /> for callers that cannot await, such as an EF Core
    /// <c>SavingChanges</c> interceptor. Blocks the calling thread for up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resource">The logical resource name, such as one built by <see cref="LockKey" />.</param>
    /// <param name="acquireTimeout">
    /// How long the engine waits for a contended lock. <see langword="null" /> and <see cref="TimeSpan.Zero" /> are
    /// one attempt; <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <returns>The held lock, or <see langword="null" /> when another session still held it as the wait elapsed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource" /> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public TransactionLockHandle? TryAcquire(string resource, TimeSpan? acquireTimeout = null)
    {
        var resources = _Single(resource);
        var timeout = _ResolveTimeout(acquireTimeout, TimeSpan.Zero);

        return _locks.TryAcquire(_unitOfWork, resources, timeout) ? new TransactionLockHandle(resource) : null;
    }

    /// <summary>
    /// Synchronous form of <see cref="AcquireAllAsync" /> for callers that cannot await, such as an EF Core
    /// <c>SavingChanges</c> interceptor. Blocks the calling thread for up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resources">
    /// The logical resource names. The sequence is enumerated once, deduplicated, and ordinal-sorted.
    /// </param>
    /// <param name="acquireTimeout">
    /// The wait budget for the whole set. <see langword="null" /> waits up to 30 seconds;
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits without bound; <see cref="TimeSpan.Zero" /> is one attempt per
    /// resource.
    /// </param>
    /// <returns>One held lock per distinct resource, in acquisition (ordinal) order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resources" /> is empty or contains a <see langword="null" />, empty, or whitespace name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">
    /// One of the resources was still held by another session when the budget ran out; none of the set's new locks
    /// remain held.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public IReadOnlyList<TransactionLockHandle> AcquireAll(
        IEnumerable<string> resources,
        TimeSpan? acquireTimeout = null
    )
    {
        var canonical = _Canonicalize(resources);
        var timeout = _ResolveTimeout(acquireTimeout, _DefaultAcquireTimeout);

        return _locks.TryAcquire(_unitOfWork, canonical, timeout)
            ? _Handles(canonical)
            : throw _TimedOut(canonical, timeout);
    }

    /// <summary>
    /// Synchronous form of <see cref="TryAcquireAllAsync" /> for callers that cannot await, such as an EF Core
    /// <c>SavingChanges</c> interceptor. Blocks the calling thread for up to <paramref name="acquireTimeout" />.
    /// </summary>
    /// <param name="resources">
    /// The logical resource names. The sequence is enumerated once, deduplicated, and ordinal-sorted.
    /// </param>
    /// <param name="acquireTimeout">
    /// The wait budget for the whole set. <see langword="null" /> and <see cref="TimeSpan.Zero" /> are one attempt per
    /// resource; <see cref="Timeout.InfiniteTimeSpan" /> waits without bound.
    /// </param>
    /// <returns>
    /// One held lock per distinct resource, in acquisition (ordinal) order; or <see langword="null" /> when another
    /// session still held one of them as the budget ran out.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resources" /> is empty or contains a <see langword="null" />, empty, or whitespace name.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="acquireTimeout" /> is negative (other than <see cref="Timeout.InfiniteTimeSpan" />) or too large.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The bound unit is no longer active, exposes no relational resource, or its transaction belongs to another
    /// provider.
    /// </exception>
    /// <exception cref="LockCleanupFailedException">
    /// The acquire failed and undoing its partial work failed too, so the transaction may still hold part of the set;
    /// roll the unit back.
    /// </exception>
    /// <exception cref="DistributedLockException">
    /// The engine refused the acquire for another lock-specific reason, such as a SQL Server deadlock victim
    /// (<see cref="DistributedLockDeadlockException" />).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The bound handle was disposed.</exception>
    public IReadOnlyList<TransactionLockHandle>? TryAcquireAll(
        IEnumerable<string> resources,
        TimeSpan? acquireTimeout = null
    )
    {
        var canonical = _Canonicalize(resources);
        var timeout = _ResolveTimeout(acquireTimeout, TimeSpan.Zero);

        return _locks.TryAcquire(_unitOfWork, canonical, timeout) ? _Handles(canonical) : null;
    }

    private static string[] _Single(string resource)
    {
        return [Argument.IsNotNullOrWhiteSpace(resource)];
    }

    // Ordinal identity and ordinal order, exactly as IDistributedLock.AcquireAllAsync canonicalizes, so a session
    // composite and a transaction composite over overlapping names take them in the same order.
    private static string[] _Canonicalize(IEnumerable<string> resources)
    {
        Argument.IsNotNull(resources);

        var materialized = new List<string>();

        foreach (var resource in resources)
        {
            materialized.Add(Argument.IsNotNullOrWhiteSpace(resource, paramName: nameof(resources)));
        }

        Argument.IsNotEmpty(materialized, paramName: nameof(resources));

        return [.. materialized.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static TimeSpan _ResolveTimeout(TimeSpan? acquireTimeout, TimeSpan defaultTimeout)
    {
        var timeout = acquireTimeout ?? defaultTimeout;

        if (timeout != Timeout.InfiniteTimeSpan)
        {
            Argument.IsPositiveOrZero(timeout, paramName: nameof(acquireTimeout));
            Argument.IsLessThan(timeout.TotalMilliseconds, int.MaxValue, paramName: nameof(acquireTimeout));
        }

        return timeout;
    }

    private static TransactionLockHandle[] _Handles(string[] canonical)
    {
        var handles = new TransactionLockHandle[canonical.Length];

        for (var i = 0; i < canonical.Length; i++)
        {
            handles[i] = new TransactionLockHandle(canonical[i]);
        }

        return handles;
    }

    private static LockAcquisitionTimeoutException _TimedOut(string[] canonical, TimeSpan timeout)
    {
        // A set of one identifies itself by its own name; the joined form exists in no backend and is diagnostic only.
        var resource = canonical.Length == 1 ? canonical[0] : string.Join('+', canonical);

        return timeout == TimeSpan.Zero
            ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
            : new LockAcquisitionTimeoutException(resource);
    }
}
