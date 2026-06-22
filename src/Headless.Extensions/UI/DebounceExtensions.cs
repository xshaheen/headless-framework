// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.UI;

/// <summary>
/// Extensions that wrap an <see cref="Action"/> so it only runs after invocations stop for a quiet
/// <c>interval</c>; rapid successive calls collapse into a single trailing-edge execution.
/// </summary>
[PublicAPI]
public static class DebounceExtensions
{
    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <returns>A debounced action that defers and coalesces calls.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action Debounce(this Action action, TimeSpan interval, TimeProvider? timeProvider = null)
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return () =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action();
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0> Debounce<T0>(this Action<T0> action, TimeSpan interval, TimeProvider? timeProvider = null)
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return arg0 =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1> Debounce<T0, T1>(
        this Action<T0, T1> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
        };
    }

    /// <summary>Wraps <paramref name="action"/> so it runs only after <paramref name="interval"/> elapses without a newer call.</summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="action">The action to debounce.</param>
    /// <param name="interval">The quiet period that must elapse after the last call before <paramref name="action"/> runs.</param>
    /// <param name="timeProvider">The time provider used to schedule the interval; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2> Debounce<T0, T1, T2>(
        this Action<T0, T1, T2> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
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
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2, T3> Debounce<T0, T1, T2, T3>(
        this Action<T0, T1, T2, T3> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2, arg3) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2, arg3);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
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
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2, T3, T4> Debounce<T0, T1, T2, T3, T4>(
        this Action<T0, T1, T2, T3, T4> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2, arg3, arg4) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2, arg3, arg4);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
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
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2, T3, T4, T5> Debounce<T0, T1, T2, T3, T4, T5>(
        this Action<T0, T1, T2, T3, T4, T5> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2, arg3, arg4, arg5) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
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
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2, T3, T4, T5, T6> Debounce<T0, T1, T2, T3, T4, T5, T6>(
        this Action<T0, T1, T2, T3, T4, T5, T6> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2, arg3, arg4, arg5, arg6) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5, arg6);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
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
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2, T3, T4, T5, T6, T7> Debounce<T0, T1, T2, T3, T4, T5, T6, T7>(
        this Action<T0, T1, T2, T3, T4, T5, T6, T7> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
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
    /// <returns>A debounced action that defers and coalesces calls, invoking <paramref name="action"/> with the latest arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
    public static Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> Debounce<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
        this Action<T0, T1, T2, T3, T4, T5, T6, T7, T8> action,
        TimeSpan interval,
        TimeProvider? timeProvider = null
    )
    {
        Argument.IsNotNull(action);

        var clock = timeProvider ?? TimeProvider.System;
        var last = 0;

        return (arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8) =>
        {
            var current = Interlocked.Increment(ref last);

            _ = clock
                .Delay(interval)
                .ContinueWith(
                    task =>
                    {
                        if (current == last)
                        {
                            action(arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
                        }

                        task.Dispose();
                    },
                    TaskScheduler.Default
                );
        };
    }
}
