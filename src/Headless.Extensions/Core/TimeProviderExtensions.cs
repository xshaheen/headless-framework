// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extension methods for <see cref="TimeProvider"/>.</summary>
[PublicAPI]
public static class TimeProviderExtensions
{
    extension(TimeProvider timeProvider)
    {
        /// <summary>
        /// Waits for the specified <paramref name="delay"/> to elapse, returning normally (rather than throwing)
        /// if the wait is canceled via <paramref name="cancellationToken"/>.
        /// </summary>
        /// <param name="delay">The amount of time to wait.</param>
        /// <param name="cancellationToken">A token that, when canceled, ends the wait early without throwing.</param>
        /// <returns>A <see cref="Task"/> that completes when the delay elapses or the wait is canceled.</returns>
        public async Task DelayUntilElapsedOrCancel(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            try
            {
                await timeProvider.Delay(delay, cancellationToken);
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
