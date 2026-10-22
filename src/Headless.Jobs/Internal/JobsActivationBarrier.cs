// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// One-shot startup gate that holds every loop able to select or claim cron work until
/// <c>JobsActivationWarmup</c> has drained one stable fingerprint snapshot.
/// </summary>
/// <remarks>
/// <b>Hosted-service start order is not the guarantee.</b> The drain runs in the background after the host has
/// started, and a consuming application may also set <c>HostOptions.ServicesStartConcurrently</c>; either way the
/// scheduler can reach its first dispatch selection while the drain is still running and could dispatch a
/// uninitialized or stale-fingerprint definition under an unverified schedule interpretation — defeating the activation
/// gate outright. This barrier makes the ordering explicit and independent of how the host starts services.
/// <para>
/// <b>A failed drain is not a signal.</b> It leaves the barrier closed and the warm-up runner retries it, so waiters keep
/// waiting rather than give up on a transient store outage.
/// </para>
/// <para>
/// <b>A failed start is a result, not a fault.</b> When <c>JobsInitializationHostedService</c> rejects the configuration
/// it carries the exception as the wait's value rather than as a faulted task: the initializer already propagates it
/// out of <c>StartAsync</c> (which aborts host startup), so waiters need to observe the failure and stay closed, not
/// raise a second exception out of a background loop — and a faulted task nothing awaits (background services
/// disabled, or the host aborting before any loop reaches its wait) would surface later as an unobserved task exception.
/// </para>
/// </remarks>
internal sealed class JobsActivationBarrier
{
    // RunContinuationsAsynchronously: the signal is raised from inside the initializer's StartAsync or the warm-up run,
    // so synchronous continuations would run every parked loop's first pass inline on the signalling thread.
    private readonly TaskCompletionSource<Exception?> _activated = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    /// <summary>
    /// <see langword="true"/> once activation completed successfully; <see langword="false"/> while it is pending or
    /// after it failed. Reads the state without waiting, for callers such as readiness that must not block on startup.
    /// </summary>
    public bool IsActivated => _activated.Task is { IsCompletedSuccessfully: true, Result: null };

    /// <summary>
    /// Waits until activation has been signalled.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait (host shutdown or membership loss).</param>
    /// <returns>
    /// <see langword="null"/> when activation completed successfully, otherwise the activation failure — in which case
    /// the caller must stay closed and select nothing.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<Exception?> WaitAsync(CancellationToken cancellationToken)
    {
        return _activated.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Opens the barrier. Subsequent signals are ignored.</summary>
    public void MarkCompleted()
    {
        _activated.TrySetResult(null);
    }

    /// <summary>Opens the barrier in the failed state so waiters stay closed. Subsequent signals are ignored.</summary>
    public void MarkFailed(Exception exception)
    {
        _activated.TrySetResult(exception);
    }
}
