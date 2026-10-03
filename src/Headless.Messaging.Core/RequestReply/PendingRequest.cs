// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// The calls of this process waiting for a reply, keyed by request id. An ended call stays as a tombstone for one more
/// timeout, so a reply arriving after it can be told apart from a reply nobody here asked for.
/// </summary>
internal sealed class PendingRequests
{
    private readonly ConcurrentDictionary<string, PendingRequestEntry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private volatile bool _closed;

    /// <summary>Gets the number of calls still waiting for their outcome.</summary>
    public int PendingCount => _entries.Values.Count(static entry => entry.State is PendingRequestState.Pending);

    /// <summary>Gets the number of tracked entries, waiting calls and tombstones alike.</summary>
    public int TrackedCount => _entries.Count;

    /// <summary>Gets whether the requester began to stop, so it accepts no new calls and sends no more requests.</summary>
    public bool IsClosed => _closed;

    /// <summary>
    /// Starts tracking <paramref name="request"/> and arms its timeout, or returns <see langword="false"/> when the
    /// requester is stopping and accepts no new calls.
    /// </summary>
    public bool TryRegister(PendingRequest request, TimeSpan dueIn, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_closed)
            {
                return false;
            }

            _entries[request.RequestId] = request.Entry;

            // Armed under the lock Close takes, so a concurrent Close sees an armed call and ends it; arming after the
            // lock would leave the timer and the token registration of a call Close already ended.
            request.Arm(this, dueIn, cancellationToken);
        }

        return true;
    }

    public bool TryGet(string requestId, [NotNullWhen(true)] out PendingRequestEntry? entry)
    {
        return _entries.TryGetValue(requestId, out entry);
    }

    /// <summary>Stops tracking a call whose request never left the process, leaving no tombstone or timer behind.</summary>
    public void Discard(PendingRequest request)
    {
        request.Discard();
        Remove(request.Entry);
        request.Entry.StopRetention();
    }

    /// <summary>Removes <paramref name="entry"/> if it is still the entry tracked under its id.</summary>
    public void Remove(PendingRequestEntry entry)
    {
        _entries.TryRemove(new KeyValuePair<string, PendingRequestEntry>(entry.RequestId, entry));
    }

    /// <summary>
    /// Refuses every new call and fails every waiting one with <see cref="RequestAbortedException"/>. Runs once, when
    /// the requesting host begins to stop.
    /// </summary>
    public void Close()
    {
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
        }

        foreach (var entry in _entries.Values)
        {
            // An entry without a call is a tombstone, whose call already ended.
            entry.Call?.TryEnd(new RequestAbortedException(entry.RequestId));
        }
    }
}

internal enum PendingRequestState
{
    Pending = 0,

    /// <summary>A reply completed the call; a further reply for it is a duplicate.</summary>
    Replied = 1,

    /// <summary>The call ended without a reply; a reply for it is late.</summary>
    Ended = 2,
}

/// <summary>
/// A call's slot in <see cref="PendingRequests"/>. It holds the live call while the call waits; once the call ends it
/// keeps only what classifies a later reply, so the tombstone does not retain the call's outcome or response.
/// </summary>
internal sealed class PendingRequestEntry(string requestId, PendingRequest call)
{
    private int _state;
    private volatile bool _prepared;
    private volatile PendingRequest? _call = call;
    private ITimer? _retentionTimer;

    public string RequestId { get; } = requestId;

    public PendingRequestState State => (PendingRequestState)Volatile.Read(ref _state);

    /// <summary>Gets whether the request's final envelope exists, so its tenant is known.</summary>
    public bool IsPrepared => _prepared;

    /// <summary>
    /// Gets the call while it waits, or <see langword="null"/> once it ended and only the tombstone is left.
    /// </summary>
    public PendingRequest? Call => _call;

    internal PendingRequests? Owner { get; set; }

    internal void MarkPrepared()
    {
        _prepared = true;
    }

    /// <summary>Moves a waiting call to <paramref name="state"/>. Only the first transition out of waiting wins.</summary>
    internal bool TryLeavePending(PendingRequestState state)
    {
        return Interlocked.CompareExchange(ref _state, (int)state, (int)PendingRequestState.Pending)
            == (int)PendingRequestState.Pending;
    }

    // Runs once, on the call's winning transition: the entry drops the call and stays as a tombstone for the retention
    // window, so a reply arriving afterwards is classified, then it is removed.
    internal void Retire(TimeProvider timeProvider, TimeSpan retention)
    {
        _call = null;
        _retentionTimer = timeProvider.CreateTimer(
            static state =>
            {
                var entry = (PendingRequestEntry)state!;
                entry.Owner?.Remove(entry);
                entry._retentionTimer?.Dispose();
            },
            this,
            retention,
            Timeout.InfiniteTimeSpan
        );
    }

    // A call that ended while its request was still being sent, such as one the shutdown aborted, is discarded with no
    // tombstone, so the retention timer has nothing left to remove.
    internal void StopRetention()
    {
        _retentionTimer?.Dispose();
    }
}

/// <summary>One call waiting for its reply. Exactly one terminal transition wins; every other one is a no-op.</summary>
internal sealed class PendingRequest
{
    // ITimer and a timer-backed CancellationTokenSource accept at most uint.MaxValue - 1 milliseconds; a longer call
    // timeout is legal on RequestOptions, so every duration handed to a timer is clamped.
    internal static readonly TimeSpan MaxTimerDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly TaskCompletionSource<object> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider;
    private ITimer? _timeoutTimer;
    private CancellationTokenRegistration _cancellation;
    private string? _tenantId;

    /// <summary>Creates a call that is not tracked yet; <see cref="PendingRequests.TryRegister"/> starts tracking it.</summary>
    /// <param name="requestId">The request's framework-owned identifier.</param>
    /// <param name="responseType">The type the reply body is read as.</param>
    /// <param name="expectedMessageName">The response contract name an ok reply must carry.</param>
    /// <param name="expectedContractVersion">The response contract version an ok reply must carry.</param>
    /// <param name="timeout">
    /// The call's timeout. It is reported by <see cref="RequestTimeoutException"/> and is also how long the entry stays as
    /// a tombstone after the call ends: a reply later than that is counted as unknown rather than late.
    /// </param>
    /// <param name="timeProvider">The clock that drives the timeout and the tombstone.</param>
    public PendingRequest(
        string requestId,
        Type responseType,
        string expectedMessageName,
        string expectedContractVersion,
        TimeSpan timeout,
        TimeProvider timeProvider
    )
    {
        RequestId = requestId;
        ResponseType = responseType;
        ExpectedMessageName = expectedMessageName;
        ExpectedContractVersion = expectedContractVersion;
        TimeoutDuration = timeout;
        _timeProvider = timeProvider;
        Entry = new PendingRequestEntry(requestId, this);
    }

    public string RequestId { get; }

    public Type ResponseType { get; }

    public string ExpectedMessageName { get; }

    public string ExpectedContractVersion { get; }

    /// <summary>Gets the call's slot in <see cref="PendingRequests"/>, which outlives the call as its tombstone.</summary>
    public PendingRequestEntry Entry { get; }

    public PendingRequestState State => Entry.State;

    /// <summary>Gets whether the request's final envelope exists, so its tenant is known.</summary>
    public bool IsPrepared => Entry.IsPrepared;

    /// <summary>Gets the tenant the request was sent under; a reply must carry exactly this tenant.</summary>
    public string? TenantId => Volatile.Read(ref _tenantId);

    /// <summary>Completes with the response object, or faults with the call's typed exception.</summary>
    public Task<object> Outcome => _outcome.Task;

    /// <summary>Records the final envelope's tenant, before the request leaves the process.</summary>
    public void Prepare(string? tenantId)
    {
        Volatile.Write(ref _tenantId, tenantId);
        Entry.MarkPrepared();
    }

    public TimeSpan TimeoutDuration { get; }

    internal static TimeSpan ClampTimerDuration(TimeSpan duration)
    {
        return duration > MaxTimerDuration ? MaxTimerDuration : duration;
    }

    internal void Arm(PendingRequests owner, TimeSpan dueIn, CancellationToken cancellationToken)
    {
        Entry.Owner = owner;
        _timeoutTimer = _timeProvider.CreateTimer(
            static state =>
            {
                var request = (PendingRequest)state!;
                request.TryEnd(new RequestTimeoutException(request.RequestId, request.TimeoutDuration));
            },
            this,
            ClampTimerDuration(dueIn),
            Timeout.InfiniteTimeSpan
        );

        if (cancellationToken.CanBeCanceled)
        {
            _cancellation = cancellationToken.UnsafeRegister(
                static (state, token) => ((PendingRequest)state!)._TryCancel(token),
                this
            );
        }
    }

    /// <summary>Claims the call for a reply. Only the first claim, made while the call is still waiting, wins.</summary>
    public bool TryClaimForReply()
    {
        if (!Entry.TryLeavePending(PendingRequestState.Replied))
        {
            return false;
        }

        _OnFinished();
        return true;
    }

    /// <summary>Completes a call this thread claimed with <see cref="TryClaimForReply"/>.</summary>
    public void CompleteClaimed(object response)
    {
        _outcome.TrySetResult(response);
    }

    /// <summary>Fails a call this thread claimed with <see cref="TryClaimForReply"/>.</summary>
    public void FailClaimed(Exception exception)
    {
        _outcome.TrySetException(exception);
    }

    /// <summary>Ends the call without a reply. Returns <see langword="false"/> when it had already ended.</summary>
    public bool TryEnd(Exception exception)
    {
        if (!_TryEndState())
        {
            return false;
        }

        _outcome.TrySetException(exception);
        _OnFinished();
        return true;
    }

    internal void Discard()
    {
        _TryEndState();
        _cancellation.Dispose();
        _timeoutTimer?.Dispose();

        // The caller is rethrowing the send failure, so nobody awaits this outcome; observe a concurrent abort so it is
        // not reported as an unobserved task exception.
        _ = _outcome.Task.Exception;
    }

    private void _TryCancel(CancellationToken token)
    {
        if (!_TryEndState())
        {
            return;
        }

        _outcome.TrySetCanceled(token);
        _OnFinished();
    }

    private bool _TryEndState()
    {
        return Entry.TryLeavePending(PendingRequestState.Ended);
    }

    // Runs once, on the winning transition: the timeout and cancellation hooks are released, and the entry is left as a
    // tombstone that no longer holds this call.
    private void _OnFinished()
    {
        _cancellation.Dispose();
        _timeoutTimer?.Dispose();
        Entry.Retire(_timeProvider, ClampTimerDuration(TimeoutDuration));
    }
}
