// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class SequenceBlockTests : TestBase
{
    [Fact]
    public async Task should_call_the_store_for_every_value_when_the_block_size_is_one()
    {
        // given
        var context = new SequenceTestContext();

        // when
        for (var i = 0; i < 3; i++)
        {
            await context.Generator.NextAsync("receipt", cancellationToken: AbortToken);
        }

        // then
        await context.Store.Received(3).IncrementAsync(new SequenceKey("", "receipt", ""), 1, 1, AbortToken);
        context.Blocks.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_hand_out_a_reserved_block_from_memory_then_reserve_the_next()
    {
        // given
        var store = new CounterStore();
        var generator = _Generator(
            store,
            new SequencePolicy
            {
                Start = 100,
                Step = 2,
                BlockSize = 5,
            }
        );

        // when
        var values = new List<long>();

        for (var i = 0; i < 7; i++)
        {
            values.Add(await generator.NextAsync("receipt", cancellationToken: AbortToken));
        }

        // then: one call created the row at the block's last value, the next advanced it by the whole block
        values.Should().Equal(100, 102, 104, 106, 108, 110, 112);
        store.Calls.Should().Equal((108L, 10L), (108L, 10L));
    }

    [Fact]
    public async Task should_reserve_one_block_per_exhaustion_when_callers_race_on_one_key()
    {
        // given
        var store = new CounterStore();
        var generator = _Generator(store, new SequencePolicy { BlockSize = 10 });

        // when
        var values = await Task.WhenAll(
            Enumerable
                .Range(0, 100)
                .Select(_ => Task.Run(async () => await generator.NextAsync("receipt", cancellationToken: AbortToken)))
        );

        // then
        values.Should().BeEquivalentTo(Enumerable.Range(1, 100).Select(static v => (long)v));
        store.Calls.Should().HaveCount(10);
    }

    [Fact]
    public async Task should_never_issue_a_value_twice_across_two_generators_sharing_a_store()
    {
        // given: two hosts, each with its own blocks, on one counter
        var store = new CounterStore();
        var policy = new SequencePolicy { BlockSize = 7 };
        var hostA = _Generator(store, policy);
        var hostB = _Generator(store, policy);

        // when
        var values = await Task.WhenAll(
            Enumerable
                .Range(0, 32)
                .Select(caller =>
                    Task.Run(async () =>
                    {
                        var host = caller % 2 == 0 ? hostA : hostB;
                        var taken = new List<long>();

                        for (var i = 0; i < 50; i++)
                        {
                            taken.Add(await host.NextAsync("receipt", cancellationToken: AbortToken));
                        }

                        return taken;
                    })
                )
        );

        // then
        var all = values.SelectMany(static v => v).ToList();
        all.Should().HaveCount(1600).And.OnlyHaveUniqueItems();
        all.Should().OnlyContain(v => v >= 1 && v <= store.Value(new SequenceKey("", "receipt", "")));
    }

    [Fact]
    public async Task should_start_a_new_block_when_a_document_number_period_resets()
    {
        // given
        var store = new CounterStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 8, 23, 0, 0, TimeSpan.Zero));
        var generator = _Generator(
            store,
            new SequencePolicy
            {
                BlockSize = 10,
                Reset = SequenceReset.Day,
                Format = "R-{yyyy}{MM}{dd}-{seq}",
            },
            clock
        );

        // when
        var first = await generator.NextNumberAsync("receipt", AbortToken);
        var second = await generator.NextNumberAsync("receipt", AbortToken);
        clock.Advance(TimeSpan.FromHours(2));
        var nextDay = await generator.NextNumberAsync("receipt", AbortToken);

        // then: the new period starts at the policy start, and the old period's unused values are never issued
        first.Text.Should().Be("R-20261008-1");
        second.Text.Should().Be("R-20261008-2");
        nextDay.Text.Should().Be("R-20261009-1");
        store.Value(new SequenceKey("", "receipt", "2026-10-08")).Should().Be(10);
        store.Value(new SequenceKey("", "receipt", "2026-10-09")).Should().Be(10);
    }

    [Fact]
    public async Task should_evict_the_coldest_key_past_the_bound_and_leave_its_unused_values_as_a_gap()
    {
        // given
        var store = new CounterStore();
        var options = new SequencesOptions
        {
            DefaultPolicy = new SequencePolicy { BlockSize = 10 },
            MaxCachedBlocks = 2,
        };
        var (generator, blocks) = _Create(store, options, new FakeTimeProvider());

        // when
        var a = await generator.NextAsync("a", cancellationToken: AbortToken);
        var b = await generator.NextAsync("b", cancellationToken: AbortToken);
        await generator.NextAsync("b", cancellationToken: AbortToken);
        var c = await generator.NextAsync("c", cancellationToken: AbortToken);
        var bAgain = await generator.NextAsync("b", cancellationToken: AbortToken);
        var aAgain = await generator.NextAsync("a", cancellationToken: AbortToken);

        // then: "a" was the coldest when "c" arrived, so its next value comes from a fresh block
        (a, b, c, bAgain)
            .Should()
            .Be((1, 1, 1, 3));
        aAgain.Should().Be(11);
        blocks.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_drop_a_block_reserved_under_another_step()
    {
        // given
        var store = new CounterStore();
        var options = new SequencesOptions { DefaultPolicy = new SequencePolicy { BlockSize = 5 } };
        var (generator, _) = _Create(store, options, new FakeTimeProvider());
        var first = await generator.NextAsync("receipt", cancellationToken: AbortToken);

        // when
        options.DefaultPolicy = new SequencePolicy { BlockSize = 5, Step = 10 };
        var afterChange = await generator.NextAsync("receipt", cancellationToken: AbortToken);

        // then: the old block's 2..5 are a gap, and the new block follows the new step
        first.Should().Be(1);
        afterChange.Should().Be(15);
        store.Calls.Should().Equal((5L, 5L), (41L, 50L));
    }

    [Fact]
    public async Task should_reserve_a_range_directly_whatever_the_block_size()
    {
        // given
        var store = new CounterStore();
        var generator = _Generator(store, new SequencePolicy { BlockSize = 10 });

        // when
        var range = await generator.ReserveAsync("batch", 3, cancellationToken: AbortToken);

        // then
        (range.First, range.Last)
            .Should()
            .Be((1, 3));
        store.Calls.Should().Equal((3L, 3L));
    }

    private static SequenceGenerator _Generator(
        CounterStore store,
        SequencePolicy policy,
        FakeTimeProvider? clock = null
    )
    {
        return _Create(
            store,
            new SequencesOptions { DefaultPolicy = policy },
            clock ?? new FakeTimeProvider()
        ).Generator;
    }

    private static (SequenceGenerator Generator, SequenceBlocks Blocks) _Create(
        CounterStore store,
        SequencesOptions options,
        FakeTimeProvider clock
    )
    {
        var monitor = Substitute.For<IOptionsMonitor<SequencesOptions>>();
        monitor.CurrentValue.Returns(_ => options);
        var blocks = new SequenceBlocks(store, monitor);
        var resolver = new SequenceRequestResolver(new MutableCurrentTenant(), monitor);

        return (new SequenceGenerator(resolver, store, blocks, clock), blocks);
    }

    /// <summary>An in-memory counter with the store's upsert semantics, recording each call's insert value and delta.</summary>
    private sealed class CounterStore : ISequenceStore
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<SequenceKey, long> _values = [];
        private readonly List<(long InsertValue, long Delta)> _calls = [];

        public IReadOnlyList<(long InsertValue, long Delta)> Calls
        {
            get
            {
                lock (_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public long Value(SequenceKey key)
        {
            lock (_lock)
            {
                return _values[key];
            }
        }

        public async ValueTask<long> IncrementAsync(
            SequenceKey key,
            long insertValue,
            long delta,
            CancellationToken cancellationToken = default
        )
        {
            // Yields so racing callers overlap the way a database round trip lets them.
            await Task.Yield();

            lock (_lock)
            {
                _calls.Add((insertValue, delta));
                var value = _values.TryGetValue(key, out var stored) ? stored + delta : insertValue;
                _values[key] = value;

                return value;
            }
        }

        public void ValidateEnlistment(IUnitOfWork unitOfWork) => throw new NotSupportedException();

        public ValueTask<long> IncrementEnlistedAsync(
            IUnitOfWork unitOfWork,
            SequenceKey key,
            long insertValue,
            long delta,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<long> AdvanceEnlistedAsync(
            IUnitOfWork unitOfWork,
            SequenceKey key,
            long value,
            long baseline,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
