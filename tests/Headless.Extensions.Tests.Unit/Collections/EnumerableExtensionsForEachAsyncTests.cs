// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;

namespace Tests.Collections;

public sealed class EnumerableExtensionsForEachAsyncTests : TestBase
{
    [Fact]
    public async Task for_each_async_should_process_items_sequentially_in_order()
    {
        // given
        var source = Enumerable.Range(1, 10).ToList();
        var processed = new List<int>();

        // when
        await source.ForEachAsync(
            async item =>
            {
                await Task.Yield();
                processed.Add(item);
            },
            AbortToken
        );

        // then - sequential contract: each invocation is awaited before the next element starts
        processed.Should().Equal(source);
    }

    [Fact]
    public async Task for_each_async_should_pass_zero_based_index_to_action()
    {
        // given
        var source = new[] { "a", "b", "c" };
        var observed = new List<(string Item, int Index)>();

        // when
        await source.ForEachAsync(
            (item, index) =>
            {
                observed.Add((item, index));
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        observed.Should().Equal(("a", 0), ("b", 1), ("c", 2));
    }

    [Fact]
    public async Task for_each_async_should_pass_the_caller_token_to_the_action()
    {
        // given
        var source = new[] { 1 };
        using var cts = new CancellationTokenSource();
        var observed = new List<CancellationToken>();

        // when
        await source.ForEachAsync(
            (_, token) =>
            {
                observed.Add(token);
                return Task.CompletedTask;
            },
            cts.Token
        );

        // then
        observed.Should().ContainSingle();
        observed.Should().HaveElementAt(0, cts.Token);
    }

    [Fact]
    public async Task for_each_async_should_throw_before_processing_when_token_already_cancelled()
    {
        // given
        var source = new[] { 1, 2, 3 };
        var processed = 0;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () =>
            source.ForEachAsync(
                _ =>
                {
                    processed++;
                    return Task.CompletedTask;
                },
                cts.Token
            );

        // then - the token is checked before each element, so nothing runs on a dead token
        await act.Should().ThrowAsync<OperationCanceledException>();
        processed.Should().Be(0);
    }

    [Fact]
    public async Task for_each_async_should_complete_when_token_cancelled_but_source_is_empty()
    {
        // given - the token is only observed per element, so an empty source never reaches a check
        var source = Array.Empty<int>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () => source.ForEachAsync(_ => Task.CompletedTask, cts.Token);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task for_each_async_should_stop_between_elements_when_cancelled_during_processing()
    {
        // given
        var source = new[] { 1, 2, 3 };
        var processed = new List<int>();
        using var cts = new CancellationTokenSource();

        // when - the action cancels while handling the first element
        var act = () =>
            source.ForEachAsync(
                async item =>
                {
                    processed.Add(item);
                    await cts.CancelAsync();
                },
                cts.Token
            );

        // then - the in-flight element finishes, the next element never starts
        await act.Should().ThrowAsync<OperationCanceledException>();
        processed.Should().Equal(1);
    }

    [Fact]
    public async Task for_each_async_should_throw_when_source_is_null()
    {
        // given
        IEnumerable<int> source = null!;

        // when
        var act = () => source.ForEachAsync(_ => Task.CompletedTask, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task for_each_async_should_throw_when_action_is_null()
    {
        // given
        var source = new[] { 1 };

        // when
        var act = () => source.ForEachAsync((Func<int, Task>)null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
