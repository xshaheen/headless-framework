// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;

namespace Tests.Internal;

public sealed class ConsumeContextAccessorTests
{
    [Fact]
    public void should_return_null_when_no_context_is_set()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor();

        // then
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_flow_current_context_across_await()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor();
        var context = _Context("corr-1");

        // when
        accessor.Current = context;
        await Task.Yield();

        // then
        accessor.Current.Should().BeSameAs(context);
    }

    [Fact]
    public void should_clear_context_after_scope()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor();

        // when
        try
        {
            accessor.Current = _Context("corr-1");
        }
        finally
        {
            accessor.Current = null;
        }

        // then
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void should_not_allocate_holder_when_clearing_empty_context()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor
        {
            // when
            Current = null,
        };

        // then
        accessor.Current.Should().BeNull();
    }

    [Fact]
    public void should_restore_previous_context_for_nested_scopes()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor();
        var outer = _Context("outer");
        var inner = _Context("inner");

        // when
        accessor.Current = outer;
        var previous = accessor.Current;
        try
        {
            accessor.Current = inner;
            accessor.Current.Should().BeSameAs(inner);
        }
        finally
        {
            accessor.Current = previous;
        }

        // then
        accessor.Current.Should().BeSameAs(outer);
    }

    [Fact]
    public void should_not_leak_between_sequential_messages()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor
        {
            // when
            Current = _Context("first"),
        };
        accessor.Current = null;
        var secondEntryValue = accessor.Current;
        accessor.Current = _Context("second");

        // then
        secondEntryValue.Should().BeNull();
        accessor.Current!.CorrelationId.Should().Be("second");
    }

    [Fact]
    public async Task should_preserve_child_task_context_when_parent_clears_current()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor();
        var context = _Context("corr-1");
        var childReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        accessor.Current = context;

        // when
        var child = Task.Run(async () =>
        {
            accessor.Current.Should().BeSameAs(context);
            childReady.TrySetResult();
            await releaseChild.Task;
            return accessor.Current;
        });

        await childReady.Task;
        accessor.Current = null;
        releaseChild.TrySetResult();
        var childContext = await child;

        // then
        accessor.Current.Should().BeNull();
        childContext.Should().BeSameAs(context);
    }

    [Fact]
    public async Task should_isolate_context_between_concurrent_sibling_tasks()
    {
        // given
        var accessor = new AsyncLocalConsumeContextAccessor();
        var aReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Two independent Task.Run roots — each gets its own AsyncLocal flow.
        var taskA = Task.Run(async () =>
        {
            accessor.Current = _Context("corr-a");
            aReady.TrySetResult();
            await release.Task;
            return accessor.Current?.CorrelationId;
        });

        var taskB = Task.Run(async () =>
        {
            accessor.Current = _Context("corr-b");
            bReady.TrySetResult();
            await release.Task;
            return accessor.Current?.CorrelationId;
        });

        // when
        await Task.WhenAll(aReady.Task, bReady.Task);
        release.TrySetResult();
        var (corrA, corrB) = (await taskA, await taskB);

        // then — each task sees only its own correlation id
        corrA.Should().Be("corr-a");
        corrB.Should().Be("corr-b");
    }

    private static ConsumeContext<TestMessage> _Context(string correlationId)
    {
        return new()
        {
            Message = new TestMessage(),
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "test",
            IntentType = IntentType.Bus,
        };
    }

    private sealed record TestMessage;
}
