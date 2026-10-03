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
