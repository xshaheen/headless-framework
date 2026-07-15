// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Collections;

public sealed class QueueExtensionsTests
{
    [Fact]
    public void should_append_collection_in_order_when_enqueue_range_enumerable()
    {
        // given - an ICollection<T> source takes the EnsureCapacity pre-sizing path
        var queue = new Queue<int>([1, 2]);
        IEnumerable<int> items = [3, 4, 5];

        // when
        queue.EnqueueRange(items);

        // then
        queue.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void should_append_lazy_sequence_in_order_when_enqueue_range_enumerable()
    {
        // given - a lazy sequence has no ICollection<T> count to pre-size from
        var queue = new Queue<int>();

        // when
        queue.EnqueueRange(Enumerable.Range(1, 3).Where(x => x > 0));

        // then
        queue.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_append_in_order_when_enqueue_range_span()
    {
        // given
        var queue = new Queue<int>();

        // when
        queue.EnqueueRange(1, 2, 3);

        // then
        queue.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_append_in_order_when_enqueue_range_list()
    {
        // given
        var queue = new Queue<int>();

        // when
        queue.EnqueueRange(new List<int> { 1, 2, 3 });

        // then
        queue.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_create_queue_in_order_when_to_queue()
    {
        // when
        var queue = Enumerable.Range(1, 3).ToQueue();

        // then
        queue.Should().Equal(1, 2, 3);
    }
}
