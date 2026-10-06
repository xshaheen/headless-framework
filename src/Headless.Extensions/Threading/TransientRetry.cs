// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Threading;

/// <summary>
/// Runs a self-contained operation again after a transient failure, waiting a short jittered delay before each retry.
/// </summary>
/// <remarks>
/// Built for work that owns everything it touches per attempt, such as a store call that opens its own connection and
/// transaction: a deadlock or serialization failure has already rolled that transaction back, so a fresh attempt can
/// succeed. Work running inside a caller's transaction must not use it, because the caller's transaction is already
/// gone and only its owner can decide whether to run the whole unit again.
/// </remarks>
[PublicAPI]
public static class TransientRetry
{
    /// <summary>The number of attempts <see cref="RunAsync{T}(Func{CancellationToken, ValueTask{T}}, Func{Exception, bool}, TimeProvider, CancellationToken)" /> makes: the first attempt plus two retries.</summary>
    public const int MaxAttempts = 3;

    // Retrying at once usually meets the same contending transaction again, so each retry waits a random slice of a
    // window that grows with the attempt number. The floor keeps two colliding callers from retrying in lockstep at
    // zero; the random spread keeps them from colliding again at the same later instant.
    private const double _MinStepMilliseconds = 10;
    private const double _MaxStepMilliseconds = 50;

    /// <summary>
    /// Runs <paramref name="operation" />, and when it throws an exception that <paramref name="isTransient" /> accepts,
    /// waits a jittered delay and runs it again, up to <see cref="MaxAttempts" /> attempts in total.
    /// </summary>
    /// <param name="operation">
    /// The work to run. Each call must start from scratch, for example by opening its own connection and transaction.
    /// </param>
    /// <param name="isTransient">Decides whether a failure is one a fresh attempt can clear.</param>
    /// <param name="timeProvider">The clock the delay between attempts waits on.</param>
    /// <param name="cancellationToken">Observed before each attempt and during each delay.</param>
    /// <returns>The result of the first attempt that succeeds.</returns>
    /// <remarks>
    /// The delay before retry <c>n</c> (1-based) is <c>n × U(10 ms, 50 ms)</c>. A failure that
    /// <paramref name="isTransient" /> rejects, or the failure of the last attempt, propagates unchanged: the caller sees
    /// the provider's own exception, not a wrapper.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken" /> was cancelled before an attempt or during a delay.
    /// </exception>
    public static ValueTask<T> RunAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        Func<Exception, bool> isTransient,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        return RunAsync(operation, isTransient, MaxAttempts, _GetRetryDelay, timeProvider, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation" />, and when it throws an exception that <paramref name="isTransient" /> accepts,
    /// waits <paramref name="retryDelay" /> and runs it again, up to <paramref name="maxAttempts" /> attempts in total.
    /// </summary>
    /// <param name="operation">
    /// The work to run. Each call must start from scratch, for example by opening its own connection and transaction.
    /// </param>
    /// <param name="isTransient">Decides whether a failure is one a fresh attempt can clear.</param>
    /// <param name="maxAttempts">The total number of attempts, the first included; <c>1</c> means no retry.</param>
    /// <param name="retryDelay">
    /// The delay to wait after the given failed attempt (1-based) before the next one. Use it when the contention the
    /// retry waits out has a different time scale than a database replay, such as a file another process briefly locks.
    /// </param>
    /// <param name="timeProvider">The clock the delay between attempts waits on.</param>
    /// <param name="cancellationToken">Observed before each attempt and during each delay.</param>
    /// <returns>The result of the first attempt that succeeds.</returns>
    /// <remarks>
    /// A failure that <paramref name="isTransient" /> rejects, or the failure of the last attempt, propagates unchanged.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts" /> is not positive.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken" /> was cancelled before an attempt or during a delay.
    /// </exception>
    public static async ValueTask<T> RunAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        Func<Exception, bool> isTransient,
        int maxAttempts,
        Func<int, TimeSpan> retryDelay,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(operation);
        Argument.IsNotNull(isTransient);
        Argument.IsPositive(maxAttempts);
        Argument.IsNotNull(retryDelay);
        Argument.IsNotNull(timeProvider);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && isTransient(ex))
            {
                // Swallowed only to retry; the last attempt's failure is never caught here, so it surfaces as thrown.
            }

            await Task.Delay(retryDelay(attempt), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <paramref name="operation" />, and when it throws an exception that <paramref name="isTransient" /> accepts,
    /// waits a jittered delay and runs it again, up to <see cref="MaxAttempts" /> attempts in total.
    /// </summary>
    /// <param name="operation">
    /// The work to run. Each call must start from scratch, for example by opening its own connection and transaction.
    /// </param>
    /// <param name="isTransient">Decides whether a failure is one a fresh attempt can clear.</param>
    /// <param name="timeProvider">The clock the delay between attempts waits on.</param>
    /// <param name="cancellationToken">Observed before each attempt and during each delay.</param>
    /// <returns>A task that completes when the first successful attempt completes.</returns>
    /// <remarks>
    /// The result-free form of
    /// <see cref="RunAsync{T}(Func{CancellationToken, ValueTask{T}}, Func{Exception, bool}, TimeProvider, CancellationToken)" />,
    /// with the same attempts, delays, predicate, and cancellation.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken" /> was cancelled before an attempt or during a delay.
    /// </exception>
    public static ValueTask RunAsync(
        Func<CancellationToken, ValueTask> operation,
        Func<Exception, bool> isTransient,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        return RunAsync(operation, isTransient, MaxAttempts, _GetRetryDelay, timeProvider, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation" />, and when it throws an exception that <paramref name="isTransient" /> accepts,
    /// waits <paramref name="retryDelay" /> and runs it again, up to <paramref name="maxAttempts" /> attempts in total.
    /// </summary>
    /// <param name="operation">
    /// The work to run. Each call must start from scratch, for example by opening its own connection and transaction.
    /// </param>
    /// <param name="isTransient">Decides whether a failure is one a fresh attempt can clear.</param>
    /// <param name="maxAttempts">The total number of attempts, the first included; <c>1</c> means no retry.</param>
    /// <param name="retryDelay">The delay to wait after the given failed attempt (1-based) before the next one.</param>
    /// <param name="timeProvider">The clock the delay between attempts waits on.</param>
    /// <param name="cancellationToken">Observed before each attempt and during each delay.</param>
    /// <returns>A task that completes when the first successful attempt completes.</returns>
    /// <remarks>
    /// The result-free form of
    /// <see cref="RunAsync{T}(Func{CancellationToken, ValueTask{T}}, Func{Exception, bool}, int, Func{int, TimeSpan}, TimeProvider, CancellationToken)" />,
    /// with the same attempts, delays, predicate, and cancellation.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts" /> is not positive.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken" /> was cancelled before an attempt or during a delay.
    /// </exception>
    public static async ValueTask RunAsync(
        Func<CancellationToken, ValueTask> operation,
        Func<Exception, bool> isTransient,
        int maxAttempts,
        Func<int, TimeSpan> retryDelay,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(operation);

        // One retry loop serves both forms, so the result-free form cannot drift from the valued one.
        await RunAsync(
                async ct =>
                {
                    await operation(ct).ConfigureAwait(false);

                    return true;
                },
                isTransient,
                maxAttempts,
                retryDelay,
                timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>The delay to wait after the given failed attempt (1-based) before the next one.</summary>
    private static TimeSpan _GetRetryDelay(int failedAttempt)
    {
#pragma warning disable CA5394 // Retry jitter only spreads contending callers apart; it needs no cryptographic randomness.
        var step = _MinStepMilliseconds + (Random.Shared.NextDouble() * (_MaxStepMilliseconds - _MinStepMilliseconds));
#pragma warning restore CA5394

        return TimeSpan.FromMilliseconds(failedAttempt * step);
    }
}
