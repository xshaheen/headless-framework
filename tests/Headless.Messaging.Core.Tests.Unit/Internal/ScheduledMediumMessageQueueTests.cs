// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Internal;

public sealed class ScheduledMediumMessageQueueTests : TestBase
{
    [Fact]
    public void unordered_items_should_reflect_all_enqueued_messages_without_removing_them()
    {
        // given
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var first = _CreateMediumMessage(1);
        var second = _CreateMediumMessage(2);

        // when
        queue.Enqueue(first, timeProvider.GetUtcNow().UtcDateTime.Ticks);
        queue.Enqueue(second, timeProvider.GetUtcNow().UtcDateTime.Ticks + TimeSpan.FromSeconds(1).Ticks);

        // then
        queue.Count.Should().Be(2);
        queue.UnorderedItems.Should().BeEquivalentTo([first, second]);
        queue.Count.Should().Be(2);
    }

    [Fact]
    public async Task get_consuming_enumerable_should_yield_due_messages_in_send_time_then_storage_id_order()
    {
        // given
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var first = _CreateMediumMessage(1);
        var second = _CreateMediumMessage(2);
        var third = _CreateMediumMessage(3);
        var dueAt = timeProvider.GetUtcNow().UtcDateTime.Ticks;

        queue.Enqueue(second, dueAt);
        queue.Enqueue(first, dueAt);
        queue.Enqueue(third, dueAt + TimeSpan.FromMilliseconds(10).Ticks);

        var enumerator = queue.GetConsumingEnumerable(AbortToken).GetAsyncEnumerator(AbortToken);

        // when
        await enumerator.MoveNextAsync();
        var yieldedFirst = enumerator.Current;
        await enumerator.MoveNextAsync();
        var yieldedSecond = enumerator.Current;

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        await enumerator.MoveNextAsync();
        var yieldedThird = enumerator.Current;

        // then
        yieldedFirst.StorageId.Should().Be(_StorageGuid(1));
        yieldedSecond.StorageId.Should().Be(_StorageGuid(2));
        yieldedThird.StorageId.Should().Be(_StorageGuid(3));
        queue.Count.Should().Be(0);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task get_consuming_enumerable_should_wait_until_future_message_is_due()
    {
        // given
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var message = _CreateMediumMessage(7);
        queue.Enqueue(message, timeProvider.GetUtcNow().UtcDateTime.Ticks + TimeSpan.FromMilliseconds(200).Ticks);

        var enumerator = queue.GetConsumingEnumerable(AbortToken).GetAsyncEnumerator(AbortToken);

        // when
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(75, AbortToken);

        // then
        moveNextTask.IsCompleted.Should().BeFalse();

        // when: 5s bound (not 1s) — under CI scheduling pressure the continuation can take
        // far longer than the logical work; the fake clock means it can never pass early
        timeProvider.Advance(TimeSpan.FromMilliseconds(250));
        await moveNextTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        enumerator.Current.StorageId.Should().Be(_StorageGuid(7));
        queue.Count.Should().Be(0);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task get_consuming_enumerable_should_reschedule_when_earlier_message_is_enqueued()
    {
        // given
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        using var queue = new ScheduledMediumMessageQueue(timeProvider);
        var late = _CreateMediumMessage(8);
        var early = _CreateMediumMessage(9);
        var now = timeProvider.GetUtcNow().UtcDateTime.Ticks;

        queue.Enqueue(late, now + TimeSpan.FromSeconds(10).Ticks);
        var enumerator = queue.GetConsumingEnumerable(AbortToken).GetAsyncEnumerator(AbortToken);

        // when
        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(25, AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        moveNextTask.IsCompleted.Should().BeFalse();

        queue.Enqueue(early, now + TimeSpan.FromSeconds(2).Ticks);
        await Task.Delay(25, AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await moveNextTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        enumerator.Current.StorageId.Should().Be(_StorageGuid(9));
        queue.Count.Should().Be(1);

        await enumerator.DisposeAsync();
    }

    private static MediumMessage _CreateMediumMessage(int storageId)
    {
        var storageGuid = _StorageGuid(storageId);

        return new MediumMessage
        {
            StorageId = storageGuid,
            Origin = new Message(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Headers.MessageId] = storageGuid.ToString("D"),
                    [Headers.MessageName] = $"message-{storageId}",
                },
                null
            ),
            Content = "{}",
            IntentType = IntentType.Bus,
        };
    }

    private static Guid _StorageGuid(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
}
