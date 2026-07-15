// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Testing.Tests;

namespace Tests.Collections;

public sealed class AsyncEnumerableExtensionsTests : TestBase
{
    // ConcatAsync tests

    [Fact]
    public async Task should_combine_two_sequences_when_concat_async()
    {
        // given
        var first = _CreateAsyncEnumerable([1, 2, 3], AbortToken);
        var second = _CreateAsyncEnumerable([4, 5, 6], AbortToken);

        // when
        var result = await first.ConcatAsync(second, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public async Task should_handle_empty_first_sequence_when_concat_async()
    {
        // given
        var first = _CreateAsyncEnumerable(Array.Empty<int>(), AbortToken);
        var second = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await first.ConcatAsync(second, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_handle_empty_second_sequence_when_concat_async()
    {
        // given
        var first = _CreateAsyncEnumerable([1, 2, 3], AbortToken);
        var second = _CreateAsyncEnumerable(Array.Empty<int>(), AbortToken);

        // when
        var result = await first.ConcatAsync(second, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    // DistinctAsync tests

    [Fact]
    public async Task should_remove_duplicates_when_distinct_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 2, 3, 3, 3, 4], AbortToken);

        // when
        var result = await source.DistinctAsync(cancellationToken: AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task should_use_custom_comparer_when_distinct_async()
    {
        // given
        var source = _CreateAsyncEnumerable(["a", "A", "b", "B"], AbortToken);

        // when
        var result = await source.DistinctAsync(StringComparer.OrdinalIgnoreCase, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().HaveCount(2);
        result.Should().Contain("a");
        result.Should().Contain("b");
    }

    // DistinctByAsync tests

    [Fact]
    public async Task should_use_key_selector_when_distinct_by_async()
    {
        // given
        var source = _CreateAsyncEnumerable(
            [new TestRecord("a", 1), new TestRecord("a", 2), new TestRecord("b", 3)],
            AbortToken
        );

        // when
        var result = await source.DistinctByAsync(x => x.Key, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().HaveCount(2);
        result[0].Value.Should().Be(1); // First "a" kept
        result[1].Value.Should().Be(3); // First "b" kept
    }

    [Fact]
    public async Task should_use_custom_comparer_when_distinct_by_async()
    {
        // given
        var source = _CreateAsyncEnumerable(
            [new TestRecord("a", 1), new TestRecord("A", 2), new TestRecord("b", 3)],
            AbortToken
        );

        // when
        var result = await source
            .DistinctByAsync(x => x.Key, StringComparer.OrdinalIgnoreCase, AbortToken)
            .ToListAsync(AbortToken);

        // then
        result.Should().HaveCount(2);
    }

    // OfTypeAsync tests

    [Fact]
    public async Task should_filter_by_type_when_of_type_async()
    {
        // given
        var source = _CreateAsyncEnumerable<object>([1, "a", 2, "b", 3], AbortToken);

        // when
        var result = await source.OfTypeAsync<object, int>(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_return_empty_for_no_matches_when_of_type_async()
    {
        // given
        var source = _CreateAsyncEnumerable<object>(["a", "b", "c"], AbortToken);

        // when
        var result = await source.OfTypeAsync<object, int>(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // SelectAsync tests

    [Fact]
    public async Task should_project_elements_when_select_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.SelectAsync(x => x * 2, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(2, 4, 6);
    }

    [Fact]
    public async Task should_handle_empty_sequence_when_select_async()
    {
        // given
        var source = _CreateAsyncEnumerable(Array.Empty<int>(), AbortToken);

        // when
        var result = await source.SelectAsync(x => x * 2, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // TakeAsync tests

    [Fact]
    public async Task should_return_first_n_elements_when_take_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.TakeAsync(3, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_return_all_when_take_async_count_exceeds_length()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.TakeAsync(10, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_return_empty_for_zero_count_when_take_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.TakeAsync(0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_empty_for_negative_count_when_take_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.TakeAsync(-1, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // TakeWhileAsync tests

    [Fact]
    public async Task should_take_while_predicate_is_true_when_take_while_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.TakeWhileAsync(x => x < 4, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_return_empty_when_take_while_async_first_fails()
    {
        // given
        var source = _CreateAsyncEnumerable([5, 4, 3, 2, 1], AbortToken);

        // when
        var result = await source.TakeWhileAsync(x => x < 4, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // SkipAsync tests

    [Fact]
    public async Task should_skip_first_n_elements_when_skip_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.SkipAsync(2, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task should_return_empty_when_skip_async_skip_exceeds_length()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.SkipAsync(10, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_all_for_zero_count_when_skip_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when - Skip(0) skips nothing and yields every element, matching LINQ Skip
        var result = await source.SkipAsync(0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task should_return_all_for_negative_count_when_skip_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when - a negative skip count is clamped to zero, matching LINQ Skip
        var result = await source.SkipAsync(-1, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    // SkipWhileAsync tests

    [Fact]
    public async Task should_skip_leading_matching_items_when_skip_while_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 2, 4], AbortToken);

        // when - skips the leading run where x < 3 (1, 2), then yields the rest including the later 2
        var result = await source.SkipWhileAsync(x => x < 3, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(3, 2, 4);
    }

    [Fact]
    public async Task should_yield_all_when_skip_while_async_first_item_fails_predicate()
    {
        // given
        var source = _CreateAsyncEnumerable([5, 4, 3, 2, 1], AbortToken);

        // when - 5 fails the predicate immediately so nothing is skipped
        var result = await source.SkipWhileAsync(x => x < 3, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(5, 4, 3, 2, 1);
    }

    // WhereAsync tests

    [Fact]
    public async Task should_filter_elements_when_where_async()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.WhereAsync(x => x % 2 == 0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(2, 4);
    }

    [Fact]
    public async Task should_return_empty_when_where_async_no_matches()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 3, 5], AbortToken);

        // when
        var result = await source.WhereAsync(x => x % 2 == 0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // WhereNotNull tests

    [Fact]
    public async Task should_filter_null_elements_when_where_not_null()
    {
        // given
        var source = _CreateAsyncEnumerable(["a", null, "b", null, "c"], AbortToken);

        // when
        var result = await source.WhereNotNull(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task should_return_empty_for_all_nulls_when_where_not_null()
    {
        // given
        var source = _CreateAsyncEnumerable(new string?[] { null, null, null }, AbortToken);

        // when
        var result = await source.WhereNotNull(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // WhereNotNullOrEmpty tests

    [Fact]
    public async Task should_filter_null_and_empty_strings_when_where_not_null_or_empty()
    {
        // given
        var source = _CreateAsyncEnumerable(["a", null, "", "b", "  ", "c"], AbortToken);

        // when
        var result = await source.WhereNotNullOrEmpty(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal("a", "b", "  ", "c"); // whitespace is kept
    }

    // WhereNotNullOrWhiteSpace tests

    [Fact]
    public async Task should_filter_null_empty_and_whitespace_when_where_not_null_or_whitespace()
    {
        // given
        var source = _CreateAsyncEnumerable(["a", null, "", "b", "  ", "c"], AbortToken);

        // when
        var result = await source.WhereNotNullOrWhiteSpace(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal("a", "b", "c");
    }

    // Cancellation tests

    [Fact]
    public async Task should_respect_cancellation_token_when_concat_async()
    {
        // given - intentionally using separate CTS to test mid-iteration cancellation
        using var cts = new CancellationTokenSource();
        var first = _CreateSlowAsyncEnumerable([1, 2, 3], TimeSpan.FromMilliseconds(50), AbortToken);
        var second = _CreateAsyncEnumerable([4, 5, 6], AbortToken);

        // when
        var items = new List<int>();
        var act = async () =>
        {
            await foreach (var item in first.ConcatAsync(second, cts.Token))
            {
                items.Add(item);
                if (items.Count == 2)
                {
                    await cts.CancelAsync();
                }
            }
        };
        await act.Should().ThrowAsync<OperationCanceledException>();

        // then
        items.Should().HaveCountLessThan(6);
    }

    // Helper methods

    private static async IAsyncEnumerable<T> _CreateAsyncEnumerable<T>(
        T[] items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<T> _CreateSlowAsyncEnumerable<T>(
        T[] items,
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(delay, cancellationToken);
            yield return item;
        }
    }

    private sealed record TestRecord(string Key, int Value);
}
