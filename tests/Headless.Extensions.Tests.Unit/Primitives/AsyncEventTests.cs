// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class AsyncEventTests
{
    private sealed class TestEventArgs : EventArgs { }

    [Fact]
    public async Task parallel_invoke_should_run_all_handlers_even_when_one_throws_synchronously()
    {
        // given - a parallel async event where the middle handler throws synchronously (before any await), surrounded
        // by handlers that record that they ran
        var asyncEvent = new AsyncEvent<TestEventArgs>(parallelInvoke: true);
        var firstRan = false;
        var lastRan = false;

        Func<object, TestEventArgs, Task> firstHandler = (_, _) =>
        {
            firstRan = true;
            return Task.CompletedTask;
        };
        Func<object, TestEventArgs, Task> throwingHandler = (_, _) => throw new InvalidOperationException("boom");
        Func<object, TestEventArgs, Task> lastHandler = (_, _) =>
        {
            lastRan = true;
            return Task.CompletedTask;
        };

        using var _1 = asyncEvent.AddHandler(firstHandler);
        using var _2 = asyncEvent.AddHandler(throwingHandler);
        using var _3 = asyncEvent.AddHandler(lastHandler);

        // when
        var exception = await Record.ExceptionAsync(() => asyncEvent.InvokeAsync(this, new TestEventArgs()));

        // then - the fault still surfaces, but every handler was invoked (no orphaned or skipped handlers)
        exception.Should().BeOfType<InvalidOperationException>();
        firstRan.Should().BeTrue();
        lastRan.Should().BeTrue();
    }

    [Fact]
    public async Task parallel_invoke_should_aggregate_multiple_synchronous_faults()
    {
        // given - two handlers that both throw synchronously
        var asyncEvent = new AsyncEvent<TestEventArgs>(parallelInvoke: true);

        Func<object, TestEventArgs, Task> throwOne = (_, _) => throw new InvalidOperationException("one");
        Func<object, TestEventArgs, Task> throwTwo = (_, _) => throw new InvalidOperationException("two");

        using var _1 = asyncEvent.AddHandler(throwOne);
        using var _2 = asyncEvent.AddHandler(throwTwo);

        // when
        var exception = await Record.ExceptionAsync(() => asyncEvent.InvokeAsync(this, new TestEventArgs()));

        // then - both faults surface together (flattened), not just the first
        exception.Should().BeOfType<AggregateException>();
        ((AggregateException)exception!).InnerExceptions.Should().HaveCount(2);
    }
}
