// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests.Primitives;

public sealed class AsyncEventTests : TestBase
{
    private sealed class TestEventArgs : EventArgs;

    [Fact]
    public async Task should_run_all_handlers_even_when_parallel_invoke_one_throws_synchronously()
    {
        // given - a parallel async event where the middle handler throws synchronously (before any await), surrounded
        // by handlers that record that they ran
        var asyncEvent = new AsyncEvent<TestEventArgs>(parallelInvoke: true);
        var firstRan = false;
        var lastRan = false;

        using var _1 = asyncEvent.AddHandler(
            (_, _, _) =>
            {
                firstRan = true;
                return default;
            }
        );
        using var _2 = asyncEvent.AddHandler((_, _, _) => throw new InvalidOperationException("boom"));
        using var _3 = asyncEvent.AddHandler(
            (_, _, _) =>
            {
                lastRan = true;
                return default;
            }
        );

        // when
        var exception = await Record.ExceptionAsync(() =>
            asyncEvent.InvokeAsync(this, new TestEventArgs(), AbortToken).AsTask()
        );

        // then - the fault still surfaces, but every handler was invoked (no orphaned or skipped handlers)
        exception.Should().BeOfType<InvalidOperationException>();
        firstRan.Should().BeTrue();
        lastRan.Should().BeTrue();
    }

    [Fact]
    public async Task should_aggregate_multiple_synchronous_faults_when_parallel_invoke()
    {
        // given - two handlers that both throw synchronously
        var asyncEvent = new AsyncEvent<TestEventArgs>(parallelInvoke: true);

        using var _1 = asyncEvent.AddHandler((_, _, _) => throw new InvalidOperationException("one"));
        using var _2 = asyncEvent.AddHandler((_, _, _) => throw new InvalidOperationException("two"));

        // when
        var exception = await Record.ExceptionAsync(() =>
            asyncEvent.InvokeAsync(this, new TestEventArgs(), AbortToken).AsTask()
        );

        // then - both faults surface together (flattened), not just the first
        exception.Should().BeOfType<AggregateException>();
        ((AggregateException)exception!).InnerExceptions.Should().HaveCount(2);
    }

    [Fact]
    public async Task should_stop_on_first_fault_when_sequential_invoke()
    {
        // given - a sequential event where the first handler throws
        var asyncEvent = new AsyncEvent<TestEventArgs>(parallelInvoke: false);
        var secondRan = false;
        using var _1 = asyncEvent.AddHandler((_, _, _) => throw new InvalidOperationException("boom"));
        using var _2 = asyncEvent.AddHandler(
            (_, _, _) =>
            {
                secondRan = true;
                return default;
            }
        );

        // when
        var act = async () => await asyncEvent.InvokeAsync(this, new TestEventArgs(), AbortToken);

        // then - the fault propagates and the later handler never runs
        await act.Should().ThrowAsync<InvalidOperationException>();
        secondRan.Should().BeFalse();
    }

    [Fact]
    public async Task should_run_all_handlers_and_isolate_faults_when_safe_invoke()
    {
        // given
        var asyncEvent = new AsyncEvent<TestEventArgs>();
        var firstRan = false;
        var lastRan = false;
        var errors = new List<Exception>();
        using var _1 = asyncEvent.AddHandler(
            (_, _, _) =>
            {
                firstRan = true;
                return default;
            }
        );
        using var _2 = asyncEvent.AddHandler((_, _, _) => throw new InvalidOperationException("boom"));
        using var _3 = asyncEvent.AddHandler(
            (_, _, _) =>
            {
                lastRan = true;
                return default;
            }
        );

        // when
        await asyncEvent.SafeInvokeAsync(this, new TestEventArgs(), errors.Add, AbortToken);

        // then - every handler ran, the fault was isolated and reported, nothing propagated
        firstRan.Should().BeTrue();
        lastRan.Should().BeTrue();
        errors.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task should_invoke_a_synchronous_handler()
    {
        // given
        var asyncEvent = new AsyncEvent<TestEventArgs>();
        var ran = false;
        using var _1 = asyncEvent.AddHandler((_, _) => ran = true);

        // when
        await asyncEvent.InvokeAsync(this, new TestEventArgs(), AbortToken);

        // then
        ran.Should().BeTrue();
    }

    [Fact]
    public async Task should_remove_only_the_disposed_registration_of_a_duplicated_handler()
    {
        // given - the SAME delegate added twice; disposal must be by registration identity, not delegate equality
        var asyncEvent = new AsyncEvent<TestEventArgs>();
        var count = 0;
        AsyncEventHandler<TestEventArgs> handler = (_, _, _) =>
        {
            count++;
            return default;
        };
        var first = asyncEvent.AddHandler(handler);
        using var _2 = asyncEvent.AddHandler(handler);

        // when - dispose one of the two registrations
        first.Dispose();
        await asyncEvent.InvokeAsync(this, new TestEventArgs(), AbortToken);

        // then - the surviving registration still fires exactly once
        count.Should().Be(1);
        asyncEvent.HasHandlers.Should().BeTrue();
    }

    [Fact]
    public void should_report_has_handlers_and_clear()
    {
        // given
        var asyncEvent = new AsyncEvent<TestEventArgs>();
        asyncEvent.HasHandlers.Should().BeFalse();

        var registration = asyncEvent.AddHandler((_, _) => { });
        asyncEvent.HasHandlers.Should().BeTrue();

        // when
        registration.Dispose();

        // then
        asyncEvent.HasHandlers.Should().BeFalse();

        // and clear on a re-populated event
        asyncEvent.AddHandler((_, _) => { });
        asyncEvent.ClearHandlers();
        asyncEvent.HasHandlers.Should().BeFalse();
    }

    [Fact]
    public async Task should_be_a_no_op_when_no_handlers()
    {
        // given
        var asyncEvent = new AsyncEvent<TestEventArgs>();
        var errors = new List<Exception>();

        // when / then - both invoke paths complete without work or error
        await asyncEvent.InvokeAsync(this, new TestEventArgs(), AbortToken);
        await asyncEvent.SafeInvokeAsync(this, new TestEventArgs(), errors.Add, AbortToken);
        errors.Should().BeEmpty();
    }
}
