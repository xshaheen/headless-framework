// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Time.Testing;

namespace Tests.Streaming;

/// <summary>Drives a <see cref="FakeTimeProvider"/> for code whose loops run on their own threads.</summary>
internal static class FakeClock
{
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan _RealLimit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Advances the clock in small steps until <paramref name="pending"/> completes, and returns its result. A loop on
    /// another thread may not have registered its next timer when the clock moves, so each step lets it catch up.
    /// </summary>
    public static Task<T> AdvanceUntilAsync<T>(
        FakeTimeProvider time,
        Task<T> pending,
        CancellationToken cancellationToken
    )
    {
        return AdvanceUntilAsync(time, pending, Step, cancellationToken);
    }

    /// <summary>Advances the clock <paramref name="step"/> at a time until <paramref name="pending"/> ends.</summary>
    public static async Task<T> AdvanceUntilAsync<T>(
        FakeTimeProvider time,
        Task<T> pending,
        TimeSpan step,
        CancellationToken cancellationToken
    )
    {
        await AdvanceUntilAsync(time, () => pending.IsCompleted, step, cancellationToken);

        return await pending;
    }

    /// <summary>Advances the clock in small steps until <paramref name="reached"/> holds.</summary>
    public static Task AdvanceUntilAsync(FakeTimeProvider time, Func<bool> reached, CancellationToken cancellationToken)
    {
        return AdvanceUntilAsync(time, reached, Step, cancellationToken);
    }

    /// <summary>Advances the clock <paramref name="step"/> at a time until <paramref name="reached"/> holds.</summary>
    public static async Task AdvanceUntilAsync(
        FakeTimeProvider time,
        Func<bool> reached,
        TimeSpan step,
        CancellationToken cancellationToken
    )
    {
        var limit = DateTime.UtcNow + _RealLimit;

        while (!reached())
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException("The expected state was not reached.");
            }

            time.Advance(step);
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>Advances the clock by <paramref name="total"/> in small steps, letting background loops run.</summary>
    public static async Task AdvanceByAsync(FakeTimeProvider time, TimeSpan total, CancellationToken cancellationToken)
    {
        for (var advanced = TimeSpan.Zero; advanced < total; advanced += Step)
        {
            time.Advance(Step);
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>Waits in real time for <paramref name="task"/>, failing the test instead of hanging it.</summary>
    public static Task WithinAsync(Task task, CancellationToken cancellationToken)
    {
        return task.WaitAsync(_RealLimit, cancellationToken);
    }

    /// <summary>Waits in real time for <paramref name="task"/>, failing the test instead of hanging it.</summary>
    public static Task<T> WithinAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        return task.WaitAsync(_RealLimit, cancellationToken);
    }
}
