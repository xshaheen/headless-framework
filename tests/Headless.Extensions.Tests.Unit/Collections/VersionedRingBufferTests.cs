// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Testing.Tests;

namespace Tests.Collections;

public sealed class VersionedRingBufferTests : TestBase
{
    [Fact]
    public void should_pass_strictly_increasing_sequences_to_the_factory()
    {
        // given
        var buffer = new VersionedRingBuffer<Entry>(10);

        // when
        var first = buffer.Add(sequence => new Entry(sequence, "a"));
        var second = buffer.Add(sequence => new Entry(sequence, "b"));

        // then
        first.Sequence.Should().Be(1);
        second.Sequence.Should().Be(2);
        buffer.Version.Should().Be(2);
        buffer.Count.Should().Be(2);
    }

    [Fact]
    public void should_list_newest_first_with_filter_and_limit()
    {
        // given
        var buffer = _Filled(10, "a", "bb", "c", "dd");

        // then
        buffer.ToListNewestFirst(null, null).Select(x => x.Name).Should().Equal("dd", "c", "bb", "a");
        buffer.ToListNewestFirst(x => x.Name.Length == 2, null).Select(x => x.Name).Should().Equal("dd", "bb");
        buffer.ToListNewestFirst(null, 2).Select(x => x.Name).Should().Equal("dd", "c");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_non_positive_limit(int limit)
    {
        var buffer = new VersionedRingBuffer<Entry>(1);

        var act = () => buffer.ToListNewestFirst(null, limit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_drop_the_oldest_item_when_full()
    {
        var buffer = _Filled(2, "a", "b", "c");

        buffer.Capacity.Should().Be(2);
        buffer.ToListNewestFirst(null, null).Select(x => x.Name).Should().Equal("c", "b");
        buffer.Version.Should().Be(3);
    }

    [Fact]
    public void should_find_and_remove_items_and_bump_the_version_only_on_a_removal()
    {
        // given
        var buffer = _Filled(10, "a", "b");

        // then
        buffer.Find(x => string.Equals(x.Name, "a", StringComparison.Ordinal))!.Sequence.Should().Be(1);
        buffer.Find(x => string.Equals(x.Name, "z", StringComparison.Ordinal)).Should().BeNull();
        buffer.Remove(x => string.Equals(x.Name, "a", StringComparison.Ordinal)).Should().BeTrue();
        buffer.Version.Should().Be(3);
        buffer.Remove(x => string.Equals(x.Name, "a", StringComparison.Ordinal)).Should().BeFalse();
        buffer.Version.Should().Be(3);
    }

    [Fact]
    public void should_clear_and_bump_the_version_only_when_something_was_removed()
    {
        var buffer = _Filled(10, "a", "b");

        buffer.Clear().Should().Be(2);
        buffer.Count.Should().Be(0);
        buffer.Version.Should().Be(3);
        buffer.Clear().Should().Be(0);
        buffer.Version.Should().Be(3);
    }

    [Fact]
    public void should_leave_the_buffer_unchanged_when_the_factory_throws()
    {
        // given
        var buffer = _Filled(10, "a");

        // when
        var act = () => buffer.Add(static _ => throw new InvalidOperationException("boom"));

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
        buffer.Version.Should().Be(1);
        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void should_reject_a_factory_that_returns_null()
    {
        var buffer = new VersionedRingBuffer<Entry>(1);

        var act = () => buffer.Add(static _ => null!);

        act.Should().Throw<InvalidOperationException>();
        buffer.Version.Should().Be(0);
    }

    [Fact]
    public async Task should_return_at_once_when_the_known_version_is_stale()
    {
        var buffer = _Filled(10, "a");

        (await buffer.WaitForChangeAsync(0, AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_complete_a_pending_wait_on_add_remove_and_clear()
    {
        // given
        var buffer = new VersionedRingBuffer<Entry>(10);
        var addWait = buffer.WaitForChangeAsync(0, AbortToken).AsTask();
        addWait.IsCompleted.Should().BeFalse();

        // when / then
        buffer.Add(sequence => new Entry(sequence, "a"));
        (await addWait).Should().Be(1);

        var removeWait = buffer.WaitForChangeAsync(1, AbortToken).AsTask();
        buffer.Remove(x => string.Equals(x.Name, "a", StringComparison.Ordinal));
        (await removeWait).Should().Be(2);

        buffer.Add(sequence => new Entry(sequence, "b"));
        var clearWait = buffer.WaitForChangeAsync(3, AbortToken).AsTask();
        buffer.Clear();
        (await clearWait).Should().Be(4);
    }

    [Fact]
    public async Task should_stop_waiting_when_canceled()
    {
        var buffer = new VersionedRingBuffer<Entry>(1);
        using var cts = new CancellationTokenSource();
        var wait = buffer.WaitForChangeAsync(0, cts.Token).AsTask();

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => wait).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_keep_every_concurrent_add_with_a_distinct_sequence()
    {
        // given
        var buffer = new VersionedRingBuffer<Entry>(1000);

        // when
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(worker =>
                    Task.Run(
                        () =>
                        {
                            for (var i = 0; i < 100; i++)
                            {
                                buffer.Add(sequence => new Entry(sequence, $"{worker}-{i}"));
                            }
                        },
                        AbortToken
                    )
                )
        );

        // then
        var items = buffer.ToListNewestFirst(null, null);
        items.Should().HaveCount(800);
        items.Select(x => x.Sequence).Should().BeInDescendingOrder().And.OnlyHaveUniqueItems();
        buffer.Version.Should().Be(800);
    }

    [Fact]
    public void should_reject_a_non_positive_capacity()
    {
        var act = () => new VersionedRingBuffer<Entry>(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static VersionedRingBuffer<Entry> _Filled(int capacity, params string[] names)
    {
        var buffer = new VersionedRingBuffer<Entry>(capacity);

        foreach (var name in names)
        {
            buffer.Add(sequence => new Entry(sequence, name));
        }

        return buffer;
    }

    private sealed record Entry(long Sequence, string Name);
}
