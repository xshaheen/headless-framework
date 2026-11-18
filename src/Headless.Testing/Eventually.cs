// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using AwesomeAssertions.Formatting;
using Headless.Checks;
using Xunit;

namespace Headless.Testing;

/// <summary>
/// Polls state that raises no signal, such as a row's status or a counter another thread increments, until a
/// condition holds, and fails the test with the last observed value when it does not hold in time.
/// </summary>
/// <remarks>
/// <para>
/// Await a signal with <see cref="HeadlessTestTaskExtensions.Bounded(Task, TimeSpan?, string?)"/> whenever the code
/// under test completes one (a <see cref="TaskCompletionSource"/>, a channel, a callback). Poll only state nothing
/// announces: a hand-written <c>while (!condition) await Task.Delay(...)</c> loop has no bound, or a bound that fails
/// with no hint of what it last saw.
/// </para>
/// <para>
/// The poll waits real time between probes. In a test that drives the code under test with a
/// <c>FakeTimeProvider</c>, do not pass that clock as <c>timeProvider</c>: the poll would stall until the test
/// advances it. Probe exceptions propagate unchanged and end the wait.
/// </para>
/// </remarks>
[PublicAPI]
public static class Eventually
{
    /// <summary>The pause between probes when no interval is given.</summary>
    /// <remarks>Short enough that a test pays little after the state changes, long enough not to spin a core.</remarks>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Probes <paramref name="condition"/> until it returns <see langword="true"/>, for at most
    /// <paramref name="timeout"/>, or until the running test or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="condition">The state to wait for; probed once immediately, then after each interval.</param>
    /// <param name="timeout">
    /// The limit; <see cref="HeadlessTestTaskExtensions.DefaultTimeout"/> when <see langword="null"/>. A ceiling for a
    /// stuck wait, not a timing assertion.
    /// </param>
    /// <param name="interval">The pause between probes; <see cref="DefaultInterval"/> when <see langword="null"/>.</param>
    /// <param name="timeProvider">The poll's own clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the wait, in addition to the running test's token.</param>
    /// <param name="conditionExpression">Filled in by the compiler, for the failure message.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    /// <exception cref="TimeoutException">The condition did not hold within the limit.</exception>
    /// <exception cref="OperationCanceledException">The running test or the wait was cancelled first.</exception>
    public static Task UntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null
    )
    {
        Argument.IsNotNull(condition);

        return _PollAsync(
            _ => Task.FromResult(condition()),
            static held => held,
            conditionExpression,
            conditionExpression: null,
            timeout,
            interval,
            timeProvider,
            cancellationToken
        );
    }

    /// <inheritdoc cref="UntilAsync(Func{bool}, TimeSpan?, TimeSpan?, TimeProvider?, CancellationToken, string?)"/>
    /// <param name="condition">
    /// The state to wait for, such as a store query. It receives a token cancelled when the limit passes, so a probe
    /// that hangs ends the wait too.
    /// </param>
    public static Task UntilAsync(
        Func<CancellationToken, Task<bool>> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null
    )
    {
        Argument.IsNotNull(condition);

        return _PollAsync(
            condition,
            static held => held,
            conditionExpression,
            conditionExpression: null,
            timeout,
            interval,
            timeProvider,
            cancellationToken
        );
    }

    /// <summary>
    /// Reads <paramref name="probe"/> until its value satisfies <paramref name="condition"/>, for at most
    /// <paramref name="timeout"/>, or until the running test or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <typeparam name="T">The type of the observed value.</typeparam>
    /// <param name="probe">Reads the current value; called once immediately, then after each interval.</param>
    /// <param name="condition">The test the value must pass.</param>
    /// <param name="timeout">
    /// The limit; <see cref="HeadlessTestTaskExtensions.DefaultTimeout"/> when <see langword="null"/>. A ceiling for a
    /// stuck wait, not a timing assertion.
    /// </param>
    /// <param name="interval">The pause between probes; <see cref="DefaultInterval"/> when <see langword="null"/>.</param>
    /// <param name="timeProvider">The poll's own clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the wait, in addition to the running test's token.</param>
    /// <param name="probeExpression">Filled in by the compiler, for the failure message.</param>
    /// <param name="conditionExpression">Filled in by the compiler, for the failure message.</param>
    /// <returns>The first value that satisfied <paramref name="condition"/>.</returns>
    /// <exception cref="TimeoutException">
    /// No value satisfied the condition within the limit. The message holds the last value read.
    /// </exception>
    /// <exception cref="OperationCanceledException">The running test or the wait was cancelled first.</exception>
    public static Task<T> UntilAsync<T>(
        Func<T> probe,
        Func<T, bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(probe))] string? probeExpression = null,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null
    )
    {
        Argument.IsNotNull(probe);
        Argument.IsNotNull(condition);

        return _PollAsync(
            _ => Task.FromResult(probe()),
            condition,
            probeExpression,
            conditionExpression,
            timeout,
            interval,
            timeProvider,
            cancellationToken
        );
    }

    /// <inheritdoc cref="UntilAsync{T}(Func{T}, Func{T, bool}, TimeSpan?, TimeSpan?, TimeProvider?, CancellationToken, string?, string?)"/>
    /// <param name="probe">
    /// Reads the current value, such as a row's status. It receives a token cancelled when the limit passes, so a
    /// probe that hangs ends the wait too.
    /// </param>
    public static Task<T> UntilAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default,
        [CallerArgumentExpression(nameof(probe))] string? probeExpression = null,
        [CallerArgumentExpression(nameof(condition))] string? conditionExpression = null
    )
    {
        Argument.IsNotNull(probe);
        Argument.IsNotNull(condition);

        return _PollAsync(
            probe,
            condition,
            probeExpression,
            conditionExpression,
            timeout,
            interval,
            timeProvider,
            cancellationToken
        );
    }

    private static async Task<T> _PollAsync<T>(
        Func<CancellationToken, Task<T>> probe,
        Func<T, bool> condition,
        string? probeExpression,
        string? conditionExpression,
        TimeSpan? timeout,
        TimeSpan? interval,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken
    )
    {
        var limit = timeout ?? HeadlessTestTaskExtensions.DefaultTimeout;
        var pause = interval ?? DefaultInterval;
        var clock = timeProvider ?? TimeProvider.System;
        Argument.IsPositive(limit);
        Argument.IsPositive(pause);

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken,
            cancellationToken
        );
        using var deadline = new CancellationTokenSource(limit, clock);
        using var probeAbort = CancellationTokenSource.CreateLinkedTokenSource(abort.Token, deadline.Token);

        var start = clock.GetTimestamp();
        var probes = 0;
        var observed = false;
        T last = default!;

        while (true)
        {
            T value;

            try
            {
                value = await probe(probeAbort.Token).ConfigureAwait(false);
            }
            // A probe cancelled by the limit, not by the test, is a stuck wait: report it as one.
            catch (OperationCanceledException e)
                when (deadline.IsCancellationRequested && !abort.IsCancellationRequested)
            {
                throw _Timeout(probeExpression, conditionExpression, limit, probes, observed, last, e);
            }

            probes++;

            if (condition(value))
            {
                return value;
            }

            observed = true;
            last = value;

            var remaining = limit - clock.GetElapsedTime(start);

            if (remaining <= TimeSpan.Zero)
            {
                throw _Timeout(probeExpression, conditionExpression, limit, probes, observed, last, inner: null);
            }

            // Clamped to what is left, so the last probe runs at the limit rather than up to one interval past it.
            await Task.Delay(pause < remaining ? pause : remaining, clock, abort.Token).ConfigureAwait(false);
        }
    }

    private static TimeoutException _Timeout<T>(
        string? probeExpression,
        string? conditionExpression,
        TimeSpan limit,
        int probes,
        bool observed,
        T last,
        Exception? inner
    )
    {
        var target = conditionExpression is null
            ? $"'{probeExpression}' to hold"
            : $"'{probeExpression}' to satisfy '{conditionExpression}'";

        var outcome = observed
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"the last of {probes} probe(s) returned {Formatter.ToString(last)}"
            )
            : "the probe did not return";

        return new TimeoutException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Waited {limit.TotalSeconds:0.###} s for {target}; {outcome}."
            ),
            inner
        );
    }
}
