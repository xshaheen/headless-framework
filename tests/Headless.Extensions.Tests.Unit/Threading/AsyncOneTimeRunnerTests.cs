// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Threading;

namespace Tests.Threading;

public sealed class AsyncOneTimeRunnerTests : TestBase
{
    [Fact]
    public async Task should_run_action_exactly_once_on_sequential_calls()
    {
        // given
        using var runner = new AsyncOneTimeRunner();
        var counter = 0;

        // when
        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        counter.Should().Be(1);
    }

    [Fact]
    public async Task should_run_action_exactly_once_across_concurrent_calls()
    {
        // given
        using var runner = new AsyncOneTimeRunner();
        var counter = 0;
        var gate = new TaskCompletionSource();
        const int concurrency = 10;

        // when — all tasks wait on the gate, then race to call RunAsync together
        var tasks = Enumerable
            .Range(0, concurrency)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await gate.Task;
                        await runner.RunAsync(
                            () =>
                            {
                                Interlocked.Increment(ref counter);
                                return Task.CompletedTask;
                            },
                            AbortToken
                        );
                    },
                    AbortToken
                )
            )
            .ToList();

        gate.SetResult();
        await Task.WhenAll(tasks);

        // then
        counter.Should().Be(1);
    }

    [Fact]
    public async Task should_retry_after_action_throws()
    {
        // given
        using var runner = new AsyncOneTimeRunner();
        var counter = 0;

        // when — first call throws; run flag must NOT be set
        var act = async () =>
            await runner.RunAsync(() => throw new InvalidOperationException("transient failure"), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // second call runs successfully
        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then — action ran once (on the second, successful call)
        counter.Should().Be(1);
    }

    [Fact]
    public async Task should_not_run_after_successful_call_even_when_action_previously_threw()
    {
        // given
        using var runner = new AsyncOneTimeRunner();
        var counter = 0;

        // first call throws
        try
        {
            await runner.RunAsync(() => throw new InvalidOperationException("transient failure"), AbortToken);
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        // second call succeeds and marks the runner done
        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        // third call must be a no-op
        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        counter.Should().Be(1);
    }

    [Fact]
    public async Task should_surface_exception_from_action_to_caller()
    {
        // given
        using var runner = new AsyncOneTimeRunner();

        // when
        var act = async () => await runner.RunAsync(() => throw new InvalidOperationException("expected"), AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("expected");
    }

    [Fact]
    public async Task should_throw_operation_canceled_exception_on_pre_cancelled_token()
    {
        // given
        using var runner = new AsyncOneTimeRunner();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await runner.RunAsync(() => Task.CompletedTask, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_operation_canceled_exception_when_token_cancelled_while_waiting()
    {
        // given
        using var runner = new AsyncOneTimeRunner();
        using var cts = new CancellationTokenSource();
        var actionStarted = new TaskCompletionSource();
        var actionCanContinue = new TaskCompletionSource();

        // hold the semaphore inside the runner via a long-running first call
        var holdingTask = Task.Run(
            async () =>
            {
                await runner.RunAsync(
                    async () =>
                    {
                        actionStarted.SetResult();
                        await actionCanContinue.Task;
                    },
                    AbortToken
                );
            },
            AbortToken
        );

        await actionStarted.Task;

        // cancel the second waiter after a short delay
        cts.CancelAfter(50);

        // when — second caller waits for the lock; token is cancelled
        var act = async () => await runner.RunAsync(() => Task.CompletedTask, cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();

        // release the holder
        actionCanContinue.SetResult();
        await holdingTask;
    }

    [Fact]
    public async Task should_throw_object_disposed_exception_after_dispose()
    {
        // given
        var runner = new AsyncOneTimeRunner();
        runner.Dispose();

        // when
        var act = async () => await runner.RunAsync(() => Task.CompletedTask, AbortToken);

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_complete_successfully_if_run_before_dispose()
    {
        // given
        var runner = new AsyncOneTimeRunner();
        var counter = 0;

        // when
        await runner.RunAsync(
            () =>
            {
                counter++;
                return Task.CompletedTask;
            },
            AbortToken
        );

        runner.Dispose();

        // then — dispose after a successful run must not throw
        counter.Should().Be(1);
    }
}
