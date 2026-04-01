// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Tests.Internal;

public sealed class ScheduledMediumMessageQueueTests
{
    [Fact]
    public void unordered_items_should_reflect_all_enqueued_messages_without_removing_them()
    {
        // given
        var timeProvider = new ManualTimeProvider();
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var first = _CreateMediumMessage(1);
        var second = _CreateMediumMessage(2);

        // when
        queue.Enqueue(first, timeProvider.CurrentTicks);
        queue.Enqueue(second, timeProvider.CurrentTicks + TimeSpan.FromSeconds(1).Ticks);

        // then
        queue.Count.Should().Be(2);
        queue.UnorderedItems.Should().BeEquivalentTo([first, second]);
        queue.Count.Should().Be(2);
    }

    [Fact]
    public async Task get_consuming_enumerable_should_yield_due_messages_in_send_time_then_storage_id_order()
    {
        // given
        var timeProvider = new ManualTimeProvider();
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var first = _CreateMediumMessage(1);
        var second = _CreateMediumMessage(2);
        var third = _CreateMediumMessage(3);
        var dueAt = timeProvider.CurrentTicks;

        queue.Enqueue(second, dueAt);
        queue.Enqueue(first, dueAt);
        queue.Enqueue(third, dueAt + TimeSpan.FromMilliseconds(10).Ticks);

        var enumerator = queue.GetConsumingEnumerable(CancellationToken.None).GetAsyncEnumerator();

        // when
        await enumerator.MoveNextAsync();
        var yieldedFirst = enumerator.Current;
        await enumerator.MoveNextAsync();
        var yieldedSecond = enumerator.Current;

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await enumerator.MoveNextAsync();
        var yieldedThird = enumerator.Current;

        // then
        yieldedFirst.StorageId.Should().Be(1);
        yieldedSecond.StorageId.Should().Be(2);
        yieldedThird.StorageId.Should().Be(3);
        queue.Count.Should().Be(0);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task get_consuming_enumerable_should_wait_until_future_message_is_due()
    {
        // given
        var timeProvider = new ManualTimeProvider();
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var message = _CreateMediumMessage(7);
        queue.Enqueue(message, timeProvider.CurrentTicks + TimeSpan.FromMilliseconds(200).Ticks);

        var enumerator = queue.GetConsumingEnumerable(CancellationToken.None).GetAsyncEnumerator();

        // when
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(75);

        // then
        moveNextTask.IsCompleted.Should().BeFalse();

        // when
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await moveNextTask.WaitAsync(TimeSpan.FromSeconds(1));

        // then
        enumerator.Current.StorageId.Should().Be(7);
        queue.Count.Should().Be(0);

        await enumerator.DisposeAsync();
    }

    private static MediumMessage _CreateMediumMessage(long storageId)
    {
        return new MediumMessage
        {
            StorageId = storageId,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = storageId.ToString(CultureInfo.InvariantCulture),
                    [Headers.MessageName] = $"message-{storageId}",
                },
                null
            ),
            Content = "{}",
        };
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        public long CurrentTicks => _utcNow.UtcDateTime.Ticks;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
    }
}
