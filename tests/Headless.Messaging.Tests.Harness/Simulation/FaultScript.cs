// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests.Simulation;

/// <summary>A storage operation a <see cref="FaultScript"/> can fault, one per <c>IDataStorage</c> method family.</summary>
[PublicAPI]
public enum StorageOperation
{
    AdmitReceivedMessage,
    IsInboxMessageAdmitted,
    DeferReceivedInboxOrphan,
    ConfirmReceivedInboxRoutable,
    GetReceivedInboxOrphansOfNeedRetry,
    GetInboxHistoryRetentionCutoffs,
    DeleteExpiredInboxAudits,
    DeleteExpiredInboxReceipts,
    ChangePublishStateToDelayed,
    ChangePublishState,
    ChangePublishRetryState,
    ReservePublishAttempt,
    RenewPublishLease,
    LeasePublish,
    LeasePublishAndReserveAttempt,
    ChangeReceiveState,
    ChangeReceiveRetryState,
    ReserveReceiveAttempt,
    RenewReceiveLease,
    LeaseReceive,
    LeaseReceiveAndReserveAttempt,
    StoreMessage,
    StoreScheduledMessage,
    StoreCoordinatedMessage,
    StoreReceivedExceptionMessage,
    StoreReceivedMessage,
    DeleteExpires,
    GetPublishedMessagesOfNeedRetry,
    ReclaimDeadPublishedOwners,
    ScheduleMessagesOfDelayed,
    GetReceivedMessagesOfNeedRetry,
    ReclaimDeadReceivedOwners,
    DeleteReceivedMessage,
    DeleteReceivedMessages,
    DeletePublishedMessage,
    DeletePublishedMessages,
    ClaimDelayedMessages,
    ReleasePublishedLease,
    ReleaseReceivedLease,
    ReleasePublishedLeases,
    ReleaseReceivedLeases,
    DeferReceivedRetry,
    Revoke,
}

/// <summary>How a <see cref="FaultScript"/> faults one storage call.</summary>
[PublicAPI]
public enum StoreFaultKind
{
    /// <summary>The call throws before it reaches the store, so nothing is written.</summary>
    Fail,

    /// <summary>The store commits the call, then the caller sees a failure, as when the commit reply is lost.</summary>
    LoseReply,

    /// <summary>The call reaches the store only after the simulation clock moves on by the fault's delay.</summary>
    DelayCommit,
}

/// <summary>How a simulated consumer client closes, set by <see cref="FaultScript.SlowCloses"/> or <see cref="FaultScript.HangCloses"/>.</summary>
[PublicAPI]
public enum CloseFaultKind
{
    None,
    Slow,
    Hung,
}

/// <summary>One armed storage fault: the call it fires on and what it does there.</summary>
/// <param name="Operation">The operation it faults.</param>
/// <param name="Kind">What it does to the call.</param>
/// <param name="CallNumber">The call of <paramref name="Operation"/>, counted from when the script was created, it fires on.</param>
/// <param name="Exception">The exception the caller sees, or <see langword="null"/> for a <see cref="SimulatedStoreException"/>.</param>
/// <param name="Delay">How far the clock must move before a <see cref="StoreFaultKind.DelayCommit"/> call commits.</param>
[PublicAPI]
public sealed record StoreFault(
    StorageOperation Operation,
    StoreFaultKind Kind,
    int CallNumber,
    Exception? Exception,
    TimeSpan Delay
);

/// <summary>
/// A per-step fault plan for one simulation: which broker receive, settlement, or close misbehaves, and which storage
/// call fails, loses its reply, or commits late. Each counted fault fires once, on the step it names.
/// </summary>
/// <remarks>
/// Call numbers count from the moment a fault is armed: <c>onCall: 1</c> is the next call of that operation, so a
/// test can arm a fault after startup without knowing how many calls bootstrap made. Arming a fault wakes the simulated
/// broker, so an idle consumer client takes a receive fault on its next poll rather than on the next message.
/// </remarks>
[PublicAPI]
public sealed class FaultScript
{
    private readonly Lock _gate = new();
    private readonly Dictionary<StorageOperation, int> _calls = [];
    private readonly List<StoreFault> _storeFaults = [];
    private readonly Dictionary<string, (int Remaining, string Reason)> _receiveFaults = new(StringComparer.Ordinal);
    private readonly List<Action> _listeners = [];
    private int _settlementFaults;
#pragma warning disable IDE0032 // Read and written under the gate; an auto property would drop the lock.
    private TimeSpan? _leaseLifetime;
#pragma warning restore IDE0032
    private CloseFaultKind _closeKind;
    private TimeSpan _closeDuration;
    private TaskCompletionSource _hungCloseRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets how long a lease the store grants survives, from the moment it is granted, regardless of renewals; null
    /// when leases behave normally.
    /// </summary>
    public TimeSpan? LeaseLifetime
    {
        get
        {
            lock (_gate)
            {
                return _leaseLifetime;
            }
        }
    }

    /// <summary>Makes the next <paramref name="times"/> receive attempts of <paramref name="subscription"/> fail.</summary>
    /// <param name="subscription">The subscription name the core opened the client for.</param>
    /// <param name="times">How many receive attempts fail, one per retry of the client.</param>
    /// <param name="reason">The failure the client reports.</param>
    public FaultScript FailReceives(string subscription, int times, string reason = "simulated receive failure")
    {
        Argument.IsNotNullOrWhiteSpace(subscription);
        Argument.IsPositive(times);

        lock (_gate)
        {
            var remaining = _receiveFaults.TryGetValue(subscription, out var armed) ? armed.Remaining : 0;
            _receiveFaults[subscription] = (remaining + times, reason);
        }

        _Notify();
        return this;
    }

    /// <summary>Makes the next <paramref name="times"/> commits, rejects, or dead-letters of a live delivery throw.</summary>
    public FaultScript FailSettlements(int times)
    {
        Argument.IsPositive(times);

        lock (_gate)
        {
            _settlementFaults += times;
        }

        return this;
    }

    /// <summary>Makes every consumer client closed from now on take <paramref name="duration"/> of simulated time.</summary>
    public FaultScript SlowCloses(TimeSpan duration)
    {
        Argument.IsPositive(duration);

        lock (_gate)
        {
            _closeKind = CloseFaultKind.Slow;
            _closeDuration = duration;
        }

        return this;
    }

    /// <summary>
    /// Makes every consumer client closed from now on hang until <see cref="ReleaseHungCloses"/>, however far the clock
    /// moves; only the messaging core's own shutdown bound ends the wait.
    /// </summary>
    public FaultScript HangCloses()
    {
        lock (_gate)
        {
            _closeKind = CloseFaultKind.Hung;
        }

        return this;
    }

    /// <summary>Lets every hung close finish and makes later closes behave normally.</summary>
    public void ReleaseHungCloses()
    {
        TaskCompletionSource release;
        lock (_gate)
        {
            _closeKind = CloseFaultKind.None;
            release = _hungCloseRelease;
            _hungCloseRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        release.TrySetResult();
    }

    /// <summary>Makes call <paramref name="onCall"/> of <paramref name="operation"/> throw before it reaches the store.</summary>
    /// <param name="operation">The storage operation to fault.</param>
    /// <param name="onCall">Which call, from now on, fails: 1 is the next one.</param>
    /// <param name="exception">The exception the caller sees; a transient <see cref="SimulatedStoreException"/> when omitted.</param>
    public FaultScript FailStore(StorageOperation operation, int onCall = 1, Exception? exception = null)
    {
        return _ArmStore(operation, StoreFaultKind.Fail, onCall, exception, TimeSpan.Zero);
    }

    /// <summary>Makes call <paramref name="onCall"/> of <paramref name="operation"/> commit, then fail as if its reply was lost.</summary>
    public FaultScript LoseStoreReply(StorageOperation operation, int onCall = 1)
    {
        return _ArmStore(operation, StoreFaultKind.LoseReply, onCall, exception: null, TimeSpan.Zero);
    }

    /// <summary>
    /// Makes call <paramref name="onCall"/> of <paramref name="operation"/> reach the store only after the simulation
    /// clock moves <paramref name="until"/> past the call.
    /// </summary>
    public FaultScript DelayStoreCommit(StorageOperation operation, TimeSpan until, int onCall = 1)
    {
        Argument.IsPositive(until);
        return _ArmStore(operation, StoreFaultKind.DelayCommit, onCall, exception: null, until);
    }

    /// <summary>
    /// Makes every lease the store grants from now on end <paramref name="lifetime"/> after it was granted, even when it
    /// is renewed, as when the store loses an owner's lease while the attempt still runs.
    /// </summary>
    public FaultScript ExpireLeasesAfter(TimeSpan lifetime)
    {
        Argument.IsPositive(lifetime);

        lock (_gate)
        {
            _leaseLifetime = lifetime;
        }

        return this;
    }

    internal void Subscribe(Action onChanged)
    {
        lock (_gate)
        {
            _listeners.Add(onChanged);
        }
    }

    internal bool TryTakeReceiveFault(string subscription, out string reason)
    {
        lock (_gate)
        {
            if (!_receiveFaults.TryGetValue(subscription, out var armed))
            {
                reason = string.Empty;
                return false;
            }

            if (armed.Remaining == 1)
            {
                _receiveFaults.Remove(subscription);
            }
            else
            {
                _receiveFaults[subscription] = (armed.Remaining - 1, armed.Reason);
            }

            reason = armed.Reason;
            return true;
        }
    }

    internal bool TryTakeSettlementFault()
    {
        lock (_gate)
        {
            if (_settlementFaults == 0)
            {
                return false;
            }

            _settlementFaults--;
            return true;
        }
    }

    internal (CloseFaultKind Kind, TimeSpan Duration, Task Release) TakeCloseFault()
    {
        lock (_gate)
        {
            return (_closeKind, _closeDuration, _hungCloseRelease.Task);
        }
    }

    /// <summary>Counts one call of <paramref name="operation"/> and returns the fault armed for it, if any.</summary>
    internal StoreFault? TakeStoreFault(StorageOperation operation)
    {
        lock (_gate)
        {
            var call = _calls.GetValueOrDefault(operation) + 1;
            _calls[operation] = call;

            var index = _storeFaults.FindIndex(fault => fault.Operation == operation && fault.CallNumber == call);
            if (index < 0)
            {
                return null;
            }

            var fault = _storeFaults[index];
            _storeFaults.RemoveAt(index);
            return fault;
        }
    }

    private FaultScript _ArmStore(
        StorageOperation operation,
        StoreFaultKind kind,
        int onCall,
        Exception? exception,
        TimeSpan delay
    )
    {
        Argument.IsInEnum(operation);
        Argument.IsPositive(onCall);

        lock (_gate)
        {
            var callNumber = _calls.GetValueOrDefault(operation) + onCall;
            _storeFaults.Add(new StoreFault(operation, kind, callNumber, exception, delay));
        }

        return this;
    }

    private void _Notify()
    {
        Action[] listeners;
        lock (_gate)
        {
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
        {
            listener();
        }
    }
}
