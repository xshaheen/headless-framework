// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Testing.Tests;

namespace Tests.Collections;

public sealed class AsyncEnumerableExtensionsTests : TestBase
{
    // ConcatAsync tests

    [Fact]
    public async Task concat_async_should_combine_two_sequences()
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
    public async Task concat_async_should_handle_empty_first_sequence()
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
    public async Task concat_async_should_handle_empty_second_sequence()
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
    public async Task distinct_async_should_remove_duplicates()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 2, 3, 3, 3, 4], AbortToken);

        // when
        var result = await source.DistinctAsync(cancellationToken: AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task distinct_async_should_use_custom_comparer()
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
    public async Task distinct_by_async_should_use_key_selector()
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
    public async Task distinct_by_async_should_use_custom_comparer()
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
    public async Task of_type_async_should_filter_by_type()
    {
        // given
        var source = _CreateAsyncEnumerable<object>([1, "a", 2, "b", 3], AbortToken);

        // when
        var result = await source.OfTypeAsync<object, int>(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task of_type_async_should_return_empty_for_no_matches()
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
    public async Task select_async_should_project_elements()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.SelectAsync(x => x * 2, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(2, 4, 6);
    }

    [Fact]
    public async Task select_async_should_handle_empty_sequence()
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
    public async Task take_async_should_return_first_n_elements()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.TakeAsync(3, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task take_async_should_return_all_when_count_exceeds_length()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.TakeAsync(10, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task take_async_should_return_empty_for_zero_count()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.TakeAsync(0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task take_async_should_return_empty_for_negative_count()
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
    public async Task take_while_async_should_take_while_predicate_is_true()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.TakeWhileAsync(x => x < 4, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task take_while_async_should_return_empty_when_first_fails()
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
    public async Task skip_async_should_skip_first_n_elements()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.SkipAsync(2, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task skip_async_should_return_empty_when_skip_exceeds_length()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.SkipAsync(10, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task skip_async_should_return_empty_for_zero_count()
    {
        // given
        // Note: Implementation returns empty when count <= 0 (differs from standard Skip behavior)
        var source = _CreateAsyncEnumerable([1, 2, 3], AbortToken);

        // when
        var result = await source.SkipAsync(0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().BeEmpty();
    }

    // SkipWhileAsync tests
    // Note: The implementation acts as a filter (WhereNot), not standard SkipWhile.
    // It skips ALL items matching the predicate, not just leading ones.

    [Fact]
    public async Task skip_while_async_should_filter_all_matching_items()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when - skips all items where x < 3 (1 and 2)
        var result = await source.SkipWhileAsync(x => x < 3, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task skip_while_async_should_filter_matching_items_throughout_sequence()
    {
        // given
        var source = _CreateAsyncEnumerable([5, 4, 3, 2, 1], AbortToken);

        // when - skips all items where x < 3 (2 and 1), keeps 5, 4, 3
        var result = await source.SkipWhileAsync(x => x < 3, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(5, 4, 3);
    }

    // WhereAsync tests

    [Fact]
    public async Task where_async_should_filter_elements()
    {
        // given
        var source = _CreateAsyncEnumerable([1, 2, 3, 4, 5], AbortToken);

        // when
        var result = await source.WhereAsync(x => x % 2 == 0, AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal(2, 4);
    }

    [Fact]
    public async Task where_async_should_return_empty_when_no_matches()
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
    public async Task where_not_null_should_filter_null_elements()
    {
        // given
        var source = _CreateAsyncEnumerable(new string?[] { "a", null, "b", null, "c" }, AbortToken);

        // when
        var result = await source.WhereNotNull(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task where_not_null_should_return_empty_for_all_nulls()
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
    public async Task where_not_null_or_empty_should_filter_null_and_empty_strings()
    {
        // given
        var source = _CreateAsyncEnumerable(new string?[] { "a", null, "", "b", "  ", "c" }, AbortToken);

        // when
        var result = await source.WhereNotNullOrEmpty(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal("a", "b", "  ", "c"); // whitespace is kept
    }

    // WhereNotNullOrWhiteSpace tests

    [Fact]
    public async Task where_not_null_or_whitespace_should_filter_null_empty_and_whitespace()
    {
        // given
        var source = _CreateAsyncEnumerable(new string?[] { "a", null, "", "b", "  ", "c" }, AbortToken);

        // when
        var result = await source.WhereNotNullOrWhiteSpace(AbortToken).ToListAsync(AbortToken);

        // then
        result.Should().Equal("a", "b", "c");
    }

    // Cancellation tests

    [Fact]
    public async Task concat_async_should_respect_cancellation_token()
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
