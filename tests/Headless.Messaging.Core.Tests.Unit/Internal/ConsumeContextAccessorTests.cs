// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using System.Reflection;

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
        var accessor = new AsyncLocalConsumeContextAccessor();

        // when
        accessor.Current = null;

        // then
        _GetHolderValue(accessor).Should().BeNull();
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
        var accessor = new AsyncLocalConsumeContextAccessor();

        // when
        accessor.Current = _Context("first");
        accessor.Current = null;
        var secondEntryValue = accessor.Current;
        accessor.Current = _Context("second");

        // then
        secondEntryValue.Should().BeNull();
        accessor.Current!.CorrelationId.Should().Be("second");
    }

    private static ConsumeContext<TestMessage> _Context(string correlationId) =>
        new()
        {
            Message = new TestMessage(),
            MessageId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
            Timestamp = DateTimeOffset.UtcNow,
            MessageName = "test",
            IntentType = IntentType.Bus,
        };

    private static object? _GetHolderValue(AsyncLocalConsumeContextAccessor accessor)
    {
        var holderField = typeof(AsyncLocalConsumeContextAccessor).GetField(
            "_holder",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var holder = holderField.GetValue(accessor)!;

        return holder.GetType().GetProperty(nameof(AsyncLocal<object>.Value))!.GetValue(holder);
    }

    private sealed record TestMessage;
}
