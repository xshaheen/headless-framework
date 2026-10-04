// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace System;

/// <summary>Extension methods for <see cref="TimeProvider"/>.</summary>
[PublicAPI]
public static class HeadlessTimeProviderExtensions
{
    extension(TimeProvider timeProvider)
    {
        /// <summary>Creates a task that completes after <paramref name="delay"/> elapses on this provider's clock.</summary>
        /// <param name="delay">The time to wait, or <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.</param>
        /// <param name="cancellationToken">A token that cancels the wait.</param>
        /// <returns>A <see cref="Task"/> that completes when the delay elapses.</returns>
        /// <remarks>
        /// Delegates to <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>. It restores the call shape
        /// the <c>Microsoft.Bcl.TimeProvider</c> package offered, which the framework no longer references because the
        /// BCL already supplies the time provider and the delay.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="delay"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>, or is longer than the
        /// maximum supported delay.
        /// </exception>
        /// <exception cref="TaskCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        public Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return Task.Delay(delay, timeProvider, cancellationToken);
        }

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that cancels after <paramref name="delay"/> elapses on this
        /// provider's clock.
        /// </summary>
        /// <param name="delay">The time before the source cancels, or <see cref="Timeout.InfiniteTimeSpan"/> for never.</param>
        /// <returns>A new source the caller owns and must dispose.</returns>
        /// <remarks>Delegates to <see cref="CancellationTokenSource(TimeSpan, TimeProvider)"/>.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="delay"/> is negative and not <see cref="Timeout.InfiniteTimeSpan"/>, or is longer than the
        /// maximum supported delay.
        /// </exception>
        [MustDisposeResource]
        public CancellationTokenSource CreateCancellationTokenSource(TimeSpan delay)
        {
            return new CancellationTokenSource(delay, timeProvider);
        }

        /// <summary>
        /// Waits for the specified <paramref name="delay"/> to elapse, returning normally (rather than throwing)
        /// if the wait is canceled via <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="delay">The amount of time to wait.</param>
        /// <param name="cancellationToken">A token that, when canceled, ends the wait early without throwing.</param>
        /// <returns>A <see cref="Task"/> that completes when the delay elapses or the wait is canceled.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="delay"/> represents a negative time interval other than <see cref="Timeout.InfiniteTimeSpan"/>,
        /// or its total milliseconds exceeds <see cref="int.MaxValue"/>. Cancellation, by contrast, is swallowed rather than thrown.
        /// </exception>
        public async Task DelayUntilElapsedOrCancel(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            try
            {
                await timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Schedules <paramref name="action"/> to run after the specified <paramref name="delay"/> elapses,
        /// using this <see cref="TimeProvider"/> to measure the delay.
        /// </summary>
        /// <param name="delay">The amount of time to wait before invoking <paramref name="action"/>.</param>
        /// <param name="action">The asynchronous callback to invoke once the delay has elapsed.</param>
        /// <param name="cancellationToken">A token that cancels both the wait and the invocation of <paramref name="action"/>.</param>
        /// <returns>A <see cref="Task"/> that completes when <paramref name="action"/> finishes executing.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="delay"/> is not positive.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled before or during the wait or invocation.</exception>
        public Task DelayedAsync(
            TimeSpan delay,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default
        )
        {
            return Task.DelayedAsync(delay, action, timeProvider, cancellationToken);
        }
    }
}
