// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Threading;

public sealed class TransientRetryTests : TestBase
{
    private static readonly Func<Exception, bool> _IsTransient = static ex => ex is TransientException;

    [Fact]
    public async Task should_return_first_result_without_waiting_when_the_first_attempt_succeeds()
    {
        // given
        var clock = new FakeTimeProvider();
        var attempts = 0;

        // when
        var result = await TransientRetry.RunAsync(
            _ =>
            {
                attempts++;
                return ValueTask.FromResult(42);
            },
            _IsTransient,
            clock,
            AbortToken
        );

        // then
        result.Should().Be(42);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_wait_the_jittered_delay_before_retrying_a_transient_failure()
    {
        // given
        var clock = new FakeTimeProvider();
        var attempts = 0;

        // when
        var task = TransientRetry
            .RunAsync(
                _ =>
                {
                    attempts++;
                    return attempts == 1 ? throw new TransientException() : ValueTask.FromResult(7);
                },
                _IsTransient,
                clock,
                AbortToken
            )
            .AsTask();

        // then: the first retry waits at least 10 ms, so it has not run yet
        attempts.Should().Be(1);
        clock.Advance(TimeSpan.FromMilliseconds(9));
        attempts.Should().Be(1);
        task.IsCompleted.Should().BeFalse();

        // and: it runs once the 50 ms ceiling of the first window has passed
        clock.Advance(TimeSpan.FromMilliseconds(41));
        (await task).Should().Be(7);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_widen_the_delay_window_with_each_attempt()
    {
        // given
        var clock = new FakeTimeProvider();
        var attempts = 0;

        var task = TransientRetry
            .RunAsync<int>(
                _ =>
                {
                    attempts++;
                    return attempts < 3 ? throw new TransientException() : ValueTask.FromResult(3);
                },
                _IsTransient,
                clock,
                AbortToken
            )
            .AsTask();

        clock.Advance(TimeSpan.FromMilliseconds(50));
        attempts.Should().Be(2);

        // when: the second retry waits 2 × U(10, 50) ms, so at least 20 ms
        clock.Advance(TimeSpan.FromMilliseconds(19));

        // then
        attempts.Should().Be(2);
        clock.Advance(TimeSpan.FromMilliseconds(81));
        (await task).Should().Be(3);
    }

    [Fact]
    public async Task should_stop_after_three_attempts_and_rethrow_the_last_original_exception()
    {
        // given
        var clock = new FakeTimeProvider();
        var thrown = new List<TransientException>();

        var task = TransientRetry
            .RunAsync<int>(
                _ =>
                {
                    var ex = new TransientException();
                    thrown.Add(ex);
                    throw ex;
                },
                _IsTransient,
                clock,
                AbortToken
            )
            .AsTask();

        // when
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.Advance(TimeSpan.FromMilliseconds(100));

        // then
        var assertion = await FluentActions.Awaiting(() => task).Should().ThrowExactlyAsync<TransientException>();
        assertion.Which.Should().BeSameAs(thrown[^1]);
        thrown.Should().HaveCount(TransientRetry.MaxAttempts);
    }

    [Fact]
    public async Task should_use_the_supplied_attempt_count_and_delay_schedule()
    {
        // given: four attempts with a 100/200/400 ms exponential schedule, as a file-lock retry would use
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var requestedDelays = new List<int>();

        var task = TransientRetry
            .RunAsync<int>(
                _ =>
                {
                    attempts++;

                    throw new TransientException();
                },
                _IsTransient,
                maxAttempts: 4,
                retryDelay: failedAttempt =>
                {
                    requestedDelays.Add(failedAttempt);

                    return TimeSpan.FromMilliseconds(100) * Math.Pow(2, failedAttempt - 1);
                },
                clock,
                AbortToken
            )
            .AsTask();

        // when: one tick short of the first delay, then each full delay in turn
        clock.Advance(TimeSpan.FromMilliseconds(99));
        var attemptsBeforeFirstDelay = attempts;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        clock.Advance(TimeSpan.FromMilliseconds(400));

        // then
        await FluentActions.Awaiting(() => task).Should().ThrowExactlyAsync<TransientException>();
        attemptsBeforeFirstDelay.Should().Be(1);
        attempts.Should().Be(4);
        requestedDelays.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task should_reject_a_non_positive_attempt_count(int maxAttempts)
    {
        // when
        var act = async () =>
            await TransientRetry.RunAsync(
                _ => ValueTask.FromResult(1),
                _IsTransient,
                maxAttempts,
                _ => TimeSpan.Zero,
                TimeProvider.System,
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_not_retry_a_failure_the_predicate_rejects()
    {
        // given
        var clock = new FakeTimeProvider();
        var attempts = 0;
        var original = new InvalidOperationException("permanent");

        // when
        var act = () =>
            TransientRetry
                .RunAsync<int>(
                    _ =>
                    {
                        attempts++;
                        throw original;
                    },
                    _IsTransient,
                    clock,
                    AbortToken
                )
                .AsTask();

        // then
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(original);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_operation_canceled_when_cancelled_during_the_delay()
    {
        // given
        var clock = new FakeTimeProvider();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var attempts = 0;

        var task = TransientRetry
            .RunAsync<int>(
                _ =>
                {
                    attempts++;
                    throw new TransientException();
                },
                _IsTransient,
                clock,
                cts.Token
            )
            .AsTask();

        // when
        await cts.CancelAsync();

        // then
        await FluentActions.Awaiting(() => task).Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_not_start_an_attempt_when_already_cancelled()
    {
        // given
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var attempts = 0;

        // when
        var act = () =>
            TransientRetry
                .RunAsync(
                    _ =>
                    {
                        attempts++;
                        return ValueTask.FromResult(1);
                    },
                    _IsTransient,
                    TimeProvider.System,
                    cts.Token
                )
                .AsTask();

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(0);
    }

    [Fact]
    public async Task should_retry_a_result_free_operation_after_the_jittered_delay()
    {
        // given
        var clock = new FakeTimeProvider();
        var attempts = 0;

        // when
        var task = TransientRetry
            .RunAsync(
                _ =>
                {
                    attempts++;
                    return attempts == 1 ? throw new TransientException() : ValueTask.CompletedTask;
                },
                _IsTransient,
                clock,
                AbortToken
            )
            .AsTask();

        // then: the first retry waits at least 10 ms and at most 50 ms
        clock.Advance(TimeSpan.FromMilliseconds(9));
        attempts.Should().Be(1);
        task.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(41));
        await task;
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task should_stop_a_result_free_operation_after_three_attempts_and_rethrow_the_last_exception()
    {
        // given
        var clock = new FakeTimeProvider();
        var thrown = new List<TransientException>();

        var task = TransientRetry
            .RunAsync(
                ValueTask (_) =>
                {
                    var ex = new TransientException();
                    thrown.Add(ex);
                    throw ex;
                },
                _IsTransient,
                clock,
                AbortToken
            )
            .AsTask();

        // when
        clock.Advance(TimeSpan.FromMilliseconds(50));
        clock.Advance(TimeSpan.FromMilliseconds(100));

        // then
        var assertion = await FluentActions.Awaiting(() => task).Should().ThrowExactlyAsync<TransientException>();
        assertion.Which.Should().BeSameAs(thrown[^1]);
        thrown.Should().HaveCount(TransientRetry.MaxAttempts);
    }

    [Fact]
    public async Task should_use_the_supplied_schedule_for_a_result_free_operation()
    {
        // given
        var clock = new FakeTimeProvider();
        var attempts = 0;

        var task = TransientRetry
            .RunAsync(
                _ =>
                {
                    attempts++;
                    return attempts < 4 ? throw new TransientException() : ValueTask.CompletedTask;
                },
                _IsTransient,
                maxAttempts: 4,
                retryDelay: failedAttempt => TimeSpan.FromMilliseconds(100) * failedAttempt,
                clock,
                AbortToken
            )
            .AsTask();

        // when
        clock.Advance(TimeSpan.FromMilliseconds(99));
        var attemptsBeforeFirstDelay = attempts;
        clock.Advance(TimeSpan.FromMilliseconds(1));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        clock.Advance(TimeSpan.FromMilliseconds(300));

        // then
        await task;
        attemptsBeforeFirstDelay.Should().Be(1);
        attempts.Should().Be(4);
    }

    [Fact]
    public async Task should_not_retry_a_result_free_failure_the_predicate_rejects()
    {
        // given
        var attempts = 0;
        var original = new InvalidOperationException("permanent");

        // when
        var act = () =>
            TransientRetry
                .RunAsync(
                    ValueTask (_) =>
                    {
                        attempts++;
                        throw original;
                    },
                    _IsTransient,
                    new FakeTimeProvider(),
                    AbortToken
                )
                .AsTask();

        // then
        (await act.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(original);
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task should_not_start_a_result_free_attempt_when_already_cancelled()
    {
        // given
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var attempts = 0;

        // when
        var act = () =>
            TransientRetry
                .RunAsync(
                    _ =>
                    {
                        attempts++;
                        return ValueTask.CompletedTask;
                    },
                    _IsTransient,
                    TimeProvider.System,
                    cts.Token
                )
                .AsTask();

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(0);
    }

    [Fact]
    public async Task should_reject_a_null_result_free_operation()
    {
        // when
        var act = async () =>
            await TransientRetry.RunAsync(
                (Func<CancellationToken, ValueTask>)null!,
                _IsTransient,
                TimeProvider.System,
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class TransientException : Exception;
}
