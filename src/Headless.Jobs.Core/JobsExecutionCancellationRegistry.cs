// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Models;

namespace Headless.Jobs;

/// <summary>
/// Per-host registry of execution-owned cancellation sources. The execution path remains the sole source disposer;
/// observers may only signal or remove the exact opaque registration returned for that execution.
/// </summary>
internal sealed class JobsExecutionCancellationRegistry
{
    private readonly Lock _mutationLock = new();
    private readonly ConcurrentDictionary<Guid, JobsExecutionCancellationRegistration> _registrations = new();

    public JobsExecutionCancellationRegistration Register(
        CancellationTokenSource cancellationSource,
        JobExecutionState context
    )
    {
        var registration = new JobsExecutionCancellationRegistration(context.JobId, cancellationSource);

        lock (_mutationLock)
        {
            if (_registrations.TryGetValue(context.JobId, out var replaced))
            {
                if (replaced.IsCompleting)
                {
                    registration.TrySignal(JobsExecutionCancellationCause.LeaseLost);
                    return registration;
                }

                replaced.TrySignal(JobsExecutionCancellationCause.LeaseLost);
                _registrations[context.JobId] = registration;
            }
            else
            {
                _registrations.TryAdd(context.JobId, registration);
            }
        }

        return registration;
    }

    public bool TrySignalDurableCancellation(JobsExecutionCancellationRegistration registration) =>
        _TrySignal(registration, JobsExecutionCancellationCause.DurableCancellation);

    public bool TrySignalHostShutdown(JobsExecutionCancellationRegistration registration) =>
        _TrySignal(registration, JobsExecutionCancellationCause.HostShutdown);

    public bool TrySignalLeaseLoss(JobsExecutionCancellationRegistration registration) =>
        _TrySignal(registration, JobsExecutionCancellationCause.LeaseLost);

    private bool _TrySignal(JobsExecutionCancellationRegistration registration, JobsExecutionCancellationCause cause)
    {
        lock (_mutationLock)
        {
            return _IsCurrent(registration) && registration.TrySignal(cause);
        }
    }

    public bool IsCurrent(JobsExecutionCancellationRegistration registration)
    {
        lock (_mutationLock)
        {
            return _IsCurrent(registration);
        }
    }

    public bool TryBeginCompletion(JobsExecutionCancellationRegistration registration)
    {
        lock (_mutationLock)
        {
            return _IsCurrent(registration) && registration.TryBeginCompletion();
        }
    }

    public bool TryRemove(JobsExecutionCancellationRegistration registration)
    {
        lock (_mutationLock)
        {
            if (!_IsCurrent(registration) || !registration.TryRemove())
            {
                return false;
            }

            _registrations.TryRemove(registration.JobId, out _);
            return true;
        }
    }

    private bool _IsCurrent(JobsExecutionCancellationRegistration registration) =>
        _registrations.TryGetValue(registration.JobId, out var current) && ReferenceEquals(current, registration);
}

/// <summary>Opaque identity for one execution-owned cancellation registration.</summary>
internal sealed class JobsExecutionCancellationRegistration(Guid jobId, CancellationTokenSource cancellationSource)
{
    private int _cause;
    private int _state;
    private readonly Lock _syncRoot = new();

    public Guid JobId { get; } = jobId;
    public JobsExecutionCancellationCause Cause => (JobsExecutionCancellationCause)Volatile.Read(ref _cause);
    private CancellationTokenSource CancellationSource { get; } = cancellationSource;
    internal bool IsCompleting => Volatile.Read(ref _state) == (int)RegistrationState.Completing;

    internal bool TrySignal(JobsExecutionCancellationCause cause)
    {
        lock (_syncRoot)
        {
            if (Volatile.Read(ref _state) != (int)RegistrationState.Executing)
            {
                return false;
            }

            var previousCause = Cause;
            if (cause == JobsExecutionCancellationCause.LeaseLost)
            {
                if (previousCause == JobsExecutionCancellationCause.LeaseLost)
                {
                    return false;
                }

                Volatile.Write(ref _cause, (int)cause);
            }
            else if (previousCause != JobsExecutionCancellationCause.None)
            {
                return false;
            }
            Volatile.Write(ref _cause, (int)cause);

            if (previousCause == JobsExecutionCancellationCause.None)
            {
                try
                {
#pragma warning disable MA0045 // Synchronous signalling keeps removal and source disposal mutually exclusive.
                    CancellationSource.Cancel();
#pragma warning restore MA0045
                }
#pragma warning disable ERP022 // Cancellation stays observable even when a consumer callback fails.
                catch (AggregateException)
                {
                    // Callback failures must not turn durable cancellation into an observer failure.
                }
#pragma warning restore ERP022
            }

            return true;
        }
    }

    internal bool TryRemove()
    {
        lock (_syncRoot)
        {
            if (Volatile.Read(ref _state) == (int)RegistrationState.Removed)
            {
                return false;
            }

            Volatile.Write(ref _state, (int)RegistrationState.Removed);
            return true;
        }
    }

    internal bool TryBeginCompletion()
    {
        lock (_syncRoot)
        {
            if (Volatile.Read(ref _state) != (int)RegistrationState.Executing)
            {
                return false;
            }

            Volatile.Write(ref _state, (int)RegistrationState.Completing);
            return true;
        }
    }

    private enum RegistrationState
    {
        Executing,
        Completing,
        Removed,
    }
}

internal enum JobsExecutionCancellationCause
{
    None,
    DurableCancellation,
    HostShutdown,
    LeaseLost,
}
