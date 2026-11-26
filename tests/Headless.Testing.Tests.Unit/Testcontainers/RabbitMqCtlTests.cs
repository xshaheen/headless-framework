// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class RabbitMqCtlTests : TestBase
{
    [Fact]
    public async Task should_fail_with_a_timeout_when_rabbitmqctl_never_exits()
    {
        // given - a stalled rabbitmqctl: the exec answers only when its token is cancelled
        var container = Substitute.For<IContainer>();
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return new ExecResult(string.Empty, string.Empty, 0);
            });

        // when
        var act = () =>
            RabbitMqCtl.ExecAsync(container, ["delete_queue", "q"], AbortToken, TimeSpan.FromMilliseconds(50));

        // then
        var thrown = await act.Should().ThrowAsync<TimeoutException>();
        thrown.Which.Message.Should().Contain("rabbitmqctl delete_queue q");
    }

    [Fact]
    public async Task should_ask_a_read_only_call_again_after_a_stall_and_return_the_answer()
    {
        // given - list_queues stalls on its first two runs and answers on the third
        var calls = 0;
        var container = Substitute.For<IContainer>();
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Interlocked.Increment(ref calls) < RabbitMqCtl.ReadOnlyAttempts)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                }

                return new ExecResult("q 0\n", string.Empty, 0);
            });

        // when
        var result = await RabbitMqCtl.ExecAsync(
            container,
            ["list_queues", "-q"],
            AbortToken,
            TimeSpan.FromMilliseconds(150)
        );

        // then
        result.Stdout.Should().Be("q 0\n");
        calls.Should().Be(RabbitMqCtl.ReadOnlyAttempts);
    }

    [Fact]
    public async Task should_not_run_a_state_changing_call_twice_when_it_stalls()
    {
        // given - delete_queue stalls
        var calls = 0;
        var container = Substitute.For<IContainer>();
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
                return new ExecResult(string.Empty, string.Empty, 0);
            });

        // when
        var act = () =>
            RabbitMqCtl.ExecAsync(container, ["delete_queue", "q"], AbortToken, TimeSpan.FromMilliseconds(50));

        // then
        await act.Should().ThrowAsync<TimeoutException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task should_report_cancellation_not_a_timeout_when_the_caller_cancels()
    {
        // given
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var container = Substitute.For<IContainer>();
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<ExecResult>(call.Arg<CancellationToken>()));

        // when
        var act = () => RabbitMqCtl.ExecAsync(container, ["list_vhosts"], cts.Token, TimeSpan.FromMinutes(1));

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_run_the_command_through_rabbitmqctl_and_return_its_result()
    {
        // given
        var container = Substitute.For<IContainer>();
        container
            .ExecAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ExecResult("hf_1\n", string.Empty, 0));

        // when
        var result = await RabbitMqCtl.ExecAsync(container, ["list_vhosts", "-q"], AbortToken);

        // then
        result.Stdout.Should().Be("hf_1\n");
        await container
            .Received(1)
            .ExecAsync(
                Arg.Is<IList<string>>(arguments =>
                    arguments.SequenceEqual(new[] { "rabbitmqctl", "list_vhosts", "-q" })
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
