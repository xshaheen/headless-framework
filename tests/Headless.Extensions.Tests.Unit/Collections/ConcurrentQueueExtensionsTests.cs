// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Tests.Collections;

public sealed class ConcurrentQueueExtensionsTests
{
    [Fact]
    public void should_append_in_order_when_enqueue_range_enumerable()
    {
        // given
        var queue = new ConcurrentQueue<int>();
        IEnumerable<int> items = [1, 2, 3];

        // when
        queue.EnqueueRange(items);

        // then
        queue.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_append_in_order_when_enqueue_range_span()
    {
        // given
        var queue = new ConcurrentQueue<int>();

        // when
        queue.EnqueueRange(1, 2, 3);

        // then
        queue.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_append_in_order_when_enqueue_range_list()
    {
        // given
        var queue = new ConcurrentQueue<int>();

        // when
        queue.EnqueueRange(new List<int> { 1, 2, 3 });

        // then
        queue.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_empty_the_queue_when_clear()
    {
        // given - Clear now resolves to the native ConcurrentQueue<T>.Clear (the shadowing extension was removed)
        var queue = new ConcurrentQueue<int>();
        queue.EnqueueRange(1, 2, 3);

        // when
        queue.Clear();

        // then
        queue.Should().BeEmpty();
    }
}
