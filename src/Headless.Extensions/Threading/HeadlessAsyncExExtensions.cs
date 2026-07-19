// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Nito.AsyncEx;

/// <summary>Timeout-aware and exception-swallowing wait helpers for Nito.AsyncEx synchronization primitives.</summary>
[PublicAPI]
public static class HeadlessAsyncExExtensions
{
    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, abandoning the wait after <paramref name="timeout"/> elapses.</summary>
    /// <param name="resetEvent">The auto-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait before the wait is cancelled.</param>
    /// <param name="cancellationToken">A token that cancels the wait before the timeout elapses.</param>
    /// <returns>A task that completes when the event is set.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="timeout"/> elapses or <paramref name="cancellationToken"/> is cancelled before the event is set.</exception>
    [DebuggerStepThrough]
    public static async Task WaitAsync(
        this AsyncAutoResetEvent resetEvent,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using var timeoutCancellationTokenSource = timeout.ToCancellationTokenSource(cancellationToken);
        await resetEvent.WaitAsync(timeoutCancellationTokenSource.Token).ConfigureAwait(false);
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, returning silently if <paramref name="timeout"/> elapses first.</summary>
    /// <param name="resetEvent">The auto-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <param name="cancellationToken">A token that cancels the wait before the timeout elapses.</param>
    /// <returns>A task that completes when the event is set, the timeout elapses, or the wait is cancelled.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(
        this AsyncAutoResetEvent resetEvent,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await resetEvent.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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
            await resetEvent.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, abandoning the wait after <paramref name="timeout"/> elapses.</summary>
    /// <param name="resetEvent">The manual-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait before the wait is cancelled.</param>
    /// <param name="cancellationToken">A token that cancels the wait before the timeout elapses.</param>
    /// <returns>A task that completes when the event is set.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="timeout"/> elapses or <paramref name="cancellationToken"/> is cancelled before the event is set.</exception>
    [DebuggerStepThrough]
    public static async Task WaitAsync(
        this AsyncManualResetEvent resetEvent,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        using var cts = timeout.ToCancellationTokenSource(cancellationToken);
        await resetEvent.WaitAsync(cts.Token).ConfigureAwait(false);
    }

    /// <summary>Waits for the <paramref name="resetEvent"/> to be set, returning silently if <paramref name="timeout"/> elapses first.</summary>
    /// <param name="resetEvent">The manual-reset event to wait on.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <param name="cancellationToken">A token that cancels the wait before the timeout elapses.</param>
    /// <returns>A task that completes when the event is set, the timeout elapses, or the wait is cancelled.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(
        this AsyncManualResetEvent resetEvent,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await resetEvent.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
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
            await resetEvent.WaitAsync(cancellationToken).ConfigureAwait(false);
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
    /// <param name="cancellationToken">A token that cancels the wait before the timeout elapses.</param>
    /// <returns>A task that completes when the count reaches zero or the timeout elapses.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before the count reaches zero (the timeout itself never throws).</exception>
    [DebuggerStepThrough]
    public static async Task WaitAsync(
        this AsyncCountdownEvent countdownEvent,
        TimeSpan timeout,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default
    )
    {
        // Drive both sides off one CTS so the loser is cancelled the moment either side completes. Otherwise
        // Task.WhenAny abandons it: the delay timer (event-signalled path) or the wait registration (timeout
        // path) lingers until it would have finished on its own.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waitTask = countdownEvent.WaitAsync(cts.Token);
        var delayTask = (timeProvider ?? TimeProvider.System).Delay(timeout, cts.Token);

        _ = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);

        // Cancelling the loser completes it as canceled (never faulted), so neither task leaks nor needs
        // separate observation; this method still returns without throwing on timeout.
        await cts.CancelAsync().ConfigureAwait(false);

        // Caller-requested cancellation is not a timeout: surface it instead of returning silently.
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Waits for the countdown <paramref name="resetEvent"/> to reach zero, returning silently if <paramref name="timeout"/> elapses first.</summary>
    /// <param name="resetEvent">The countdown event to wait on.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <param name="cancellationToken">A token that cancels the wait before the timeout elapses.</param>
    /// <returns>A task that completes when the count reaches zero, the timeout elapses, or the wait is cancelled.</returns>
    [DebuggerStepThrough]
    public static async Task SafeWaitAsync(
        this AsyncCountdownEvent resetEvent,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await resetEvent.WaitAsync(timeout, timeProvider: null, cancellationToken).ConfigureAwait(false);
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
            await resetEvent.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }
}
