// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Models;

namespace Headless.Jobs;

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
                    CancellationSource.Cancel();
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
