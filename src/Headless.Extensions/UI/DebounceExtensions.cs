// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.UI;

/// <summary>
/// Extensions that wrap an <see cref="Action"/> so it only runs after invocations stop for a quiet
/// <c>interval</c>; rapid successive calls collapse into a single trailing-edge execution.
/// </summary>
/// <remarks>
/// Each wrapper keeps a single pending <see cref="ITimer"/> as state plus a generation counter. Every call
/// disposes the previous timer and schedules a fresh one, so only the last call within a quiet window survives
/// to run. Because disposing an <see cref="ITimer"/> does not stop a callback that is already queued on the
/// thread pool, every scheduled callback re-checks the generation it was created under and skips itself when a
/// newer call has superseded it. Exceptions thrown by the wrapped action are routed to the optional
/// <c>onError</c> handler, or swallowed when none is supplied; they never propagate out of the timer callback to
/// crash the thread pool. The returned wrapper is safe to invoke concurrently.
/// </remarks>
[PublicAPI]
public static class DebounceExtensions
{
    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action Debounce(
        this Action action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return () =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action();
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0> Debounce<T0>(
        this Action<T0> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return arg0 =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1> Debounce<T0, T1>(
        this Action<T0, T1> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2> Debounce<T0, T1, T2>(
        this Action<T0, T1, T2> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2, T3> Debounce<T0, T1, T2, T3>(
        this Action<T0, T1, T2, T3> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2, arg3) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2, arg3);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2, T3, T4> Debounce<T0, T1, T2, T3, T4>(
        this Action<T0, T1, T2, T3, T4> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2, arg3, arg4) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2, arg3, arg4);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <typeparam name="T5">The type of the sixth argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2, T3, T4, T5> Debounce<T0, T1, T2, T3, T4, T5>(
        this Action<T0, T1, T2, T3, T4, T5> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2, arg3, arg4, arg5) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <typeparam name="T5">The type of the sixth argument.</typeparam>
    /// <typeparam name="T6">The type of the seventh argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2, T3, T4, T5, T6> Debounce<T0, T1, T2, T3, T4, T5, T6>(
        this Action<T0, T1, T2, T3, T4, T5, T6> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2, arg3, arg4, arg5, arg6) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5, arg6);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <typeparam name="T5">The type of the sixth argument.</typeparam>
    /// <typeparam name="T6">The type of the seventh argument.</typeparam>
    /// <typeparam name="T7">The type of the eighth argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2, T3, T4, T5, T6, T7> Debounce<T0, T1, T2, T3, T4, T5, T6, T7>(
        this Action<T0, T1, T2, T3, T4, T5, T6, T7> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <typeparam name="T5">The type of the sixth argument.</typeparam>
    /// <typeparam name="T6">The type of the seventh argument.</typeparam>
    /// <typeparam name="T7">The type of the eighth argument.</typeparam>
    /// <typeparam name="T8">The type of the ninth argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="onError">An optional handler invoked with any exception thrown by <paramref name="action"/>; when <see langword="null"/> the exception is swallowed so a faulting action never crashes the timer's thread-pool callback.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
    public static Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> Debounce<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
        this Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null,
        Action<Exception>? onError = null
    )
    {
        Argument.IsNotNull(action);
        Argument.IsPositive(interval);

        var clock = timeProvider ?? TimeProvider.System;
        var gate = new Lock();
        ITimer? timer = null;
        var gen = new int[1];

        return (arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) =>
        {
            lock (gate)
            {
                // Cancel any pending schedule and bump the generation so a stale, already-queued callback is skipped.
                timer?.Dispose();
                var expected = Interlocked.Increment(ref gen[0]);
                timer = clock.CreateTimer(
                    _ =>
                    {
                        if (Volatile.Read(ref gen[0]) != expected)
                        {
                            return;
                        }

                        try
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
                        }
                        catch (Exception ex)
                        {
                            onError?.Invoke(ex);
                        }
                    },
                    state: null,
                    interval,
                    Timeout.InfiniteTimeSpan
                );
            }
        };
    }
}
