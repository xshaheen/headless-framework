// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// One-shot startup gate that holds every loop able to select or claim cron work until
/// <c>JobsInitializationHostedService</c> has drained one stable fingerprint snapshot.
/// </summary>
/// <remarks>
/// <b>Hosted-service registration order is not the guarantee.</b> A consuming application may set
/// <see cref="Microsoft.Extensions.Hosting.HostOptions.ServicesStartConcurrently"/>, which starts every hosted service
/// at once; the scheduler would then begin dispatch selection while the activation drain is still running and could
/// dispatch a uninitialized or stale-fingerprint definition under an unverified schedule interpretation — defeating the
/// activation gate outright. This barrier makes the ordering explicit and independent of how the host starts services.
/// <para>
/// <b>Failure is a result, not a fault.</b> The activation exception is carried as the wait's value rather than as a
/// faulted task: the initializer already propagates it out of <c>StartAsync</c> (which aborts host startup), so waiters
/// need to observe the failure and stay closed, not raise a second exception out of a background loop — and a faulted
/// task nothing awaits (background services disabled, or the host aborting before any loop reaches its wait) would
/// surface later as an unobserved task exception.
/// </para>
/// </remarks>
internal sealed class JobsActivationBarrier
{
    // RunContinuationsAsynchronously: the signal is raised from inside the initializer's StartAsync, so synchronous
    // continuations would run every parked scheduler loop's first pass inline on the host-startup thread.
    private readonly TaskCompletionSource<Exception?> _activated = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

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
