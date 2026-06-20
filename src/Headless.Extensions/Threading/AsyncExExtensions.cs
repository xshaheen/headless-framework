// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Nito.AsyncEx;

/// <summary>Timeout-aware and exception-swallowing wait helpers for Nito.AsyncEx synchronization primitives.</summary>
[PublicAPI]
public static class AsyncExExtensions
{
    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, abandoning the wait after <paramref name="timeout"/> elapses.</summary>
    /// <param name="resetEvent">The auto-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait before the wait is cancelled.</param>
    /// <returns>A task that completes when the event is set.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="timeout"/> elapses before the event is set.</exception>
    [DebuggerStepThrough]
    public static async Task WaitAsync(this AsyncAutoResetEvent resetEvent, TimeSpan timeout)
    {
        using var timeoutCancellationTokenSource = timeout.ToCancellationTokenSource();
        await resetEvent.WaitAsync(timeoutCancellationTokenSource.Token).ConfigureAwait(false);
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, returning silently if <paramref name="timeout"/> elapses first.</summary>
    /// <param name="resetEvent">The auto-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the event is set or the timeout elapses.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncAutoResetEvent resetEvent, TimeSpan timeout)
    {
        try
        {
            await resetEvent.WaitAsync(timeout);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, returning silently if the wait is cancelled.</summary>
    /// <param name="resetEvent">The auto-reset event to wait on.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when the event is set or the wait is cancelled.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncAutoResetEvent resetEvent, CancellationToken cancellationToken)
    {
        try
        {
            await resetEvent.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, abandoning the wait after <paramref name="timeout"/> elapses.</summary>
    /// <param name="resetEvent">The manual-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait before the wait is cancelled.</param>
    /// <returns>A task that completes when the event is set.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="timeout"/> elapses before the event is set.</exception>
    [DebuggerStepThrough]
    public static async Task WaitAsync(this AsyncManualResetEvent resetEvent, TimeSpan timeout)
    {
        using var cts = timeout.ToCancellationTokenSource();
        await resetEvent.WaitAsync(cts.Token).ConfigureAwait(false);
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, returning silently if <paramref name="timeout"/> elapses first.</summary>
    /// <param name="resetEvent">The manual-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the event is set or the timeout elapses.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncManualResetEvent resetEvent, TimeSpan timeout)
    {
        try
        {
            await resetEvent.WaitAsync(timeout);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, returning silently if the wait is cancelled.</summary>
    /// <param name="resetEvent">The manual-reset event to wait on.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when the event is set or the wait is cancelled.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncManualResetEvent resetEvent, CancellationToken cancellationToken)
    {
        try
        {
            await resetEvent.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Waits for the <paramref name="countdownEvent"/> to reach zero, returning when either the event signals or
    /// <paramref name="timeout"/> elapses (whichever happens first). The timeout does not throw.
    /// </summary>
    /// <param name="countdownEvent">The countdown event to wait on.</param>
    /// <param name="timeout">The maximum time to wait before the wait completes regardless of the count.</param>
    /// <param name="timeProvider">The time provider used to schedule the timeout; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <returns>A task that completes when the count reaches zero or the timeout elapses.</returns>
    [DebuggerStepThrough]
    public static async Task WaitAsync(
        this AsyncCountdownEvent countdownEvent,
        TimeSpan timeout,
        TimeProvider? timeProvider = null
    )
    {
        _ = await Task.WhenAny(countdownEvent.WaitAsync(), (timeProvider ?? TimeProvider.System).Delay(timeout))
            .ConfigureAwait(false);
    }

    /// <summary>Waits for the countdown <paramref name="resetEvent"/> to reach zero, returning silently if <paramref name="timeout"/> elapses first.</summary>
    /// <param name="resetEvent">The countdown event to wait on.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the count reaches zero or the timeout elapses.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncCountdownEvent resetEvent, TimeSpan timeout)
    {
        try
        {
            await resetEvent.WaitAsync(timeout);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Waits for the countdown <paramref name="resetEvent"/> to reach zero, returning silently if the wait is cancelled.</summary>
    /// <param name="resetEvent">The countdown event to wait on.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when the count reaches zero or the wait is cancelled.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(this AsyncCountdownEvent resetEvent, CancellationToken cancellationToken)
    {
        try
        {
            await resetEvent.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }
}
