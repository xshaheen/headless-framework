// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Testing.Tests;
using Headless.Threading;

namespace Tests.Collections;

public sealed class EnumerableExtensionsParallelForEachAsyncTests : TestBase
{
    [Fact]
    public async Task parallel_for_each_async_should_process_every_item()
    {
        // given
        var source = Enumerable.Range(1, 50).ToList();
        var processed = new ConcurrentBag<int>();

        // when
        await source.ParallelForEachAsync(
            item =>
            {
                processed.Add(item);
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then - completion is guaranteed for every element; ordering is not part of the contract
        processed.Should().BeEquivalentTo(source);
    }

    [Fact]
    public async Task parallel_for_each_async_should_not_exceed_degree_of_parallelism()
    {
        // given
        const int degreeOfParallelism = 2;
        var source = Enumerable.Range(1, 16).ToList();
        var current = 0L;
        var peak = 0L;
        var processed = 0L;

        // when
        await source.ParallelForEachAsync(
            degreeOfParallelism,
            async _ =>
            {
                var now = Interlocked.Increment(ref current);
                peak.InterlockedRaiseTo(now);
                await Task.Yield();
                Interlocked.Increment(ref processed);
                Interlocked.Decrement(ref current);
            },
            AbortToken
        );

        // then - the in-flight invocation count must never exceed the requested bound
        processed.Should().Be(source.Count);
        peak.Should().BeLessThanOrEqualTo(degreeOfParallelism);
    }

    [Fact]
    public async Task parallel_for_each_async_should_accept_minus_one_as_unlimited_parallelism()
    {
        // given - the documented sentinel for "unlimited"
        var source = Enumerable.Range(1, 10).ToList();
        var processed = 0L;

        // when
        await source.ParallelForEachAsync(
            -1,
            _ =>
            {
                Interlocked.Increment(ref processed);
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        processed.Should().Be(source.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task parallel_for_each_async_should_reject_invalid_degree_of_parallelism(int degreeOfParallelism)
    {
        // given
        var source = new[] { 1 };

        // when
        var act = () => source.ParallelForEachAsync(degreeOfParallelism, _ => Task.CompletedTask, AbortToken);

        // then - documented contract: 0 or < -1 is out of range
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task parallel_for_each_async_should_throw_without_processing_when_token_already_cancelled()
    {
        // given
        var source = Enumerable.Range(1, 5).ToList();
        var processed = 0L;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = () =>
            source.ParallelForEachAsync(
                _ =>
                {
                    Interlocked.Increment(ref processed);
                    return Task.CompletedTask;
                },
                cts.Token
            );

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        processed.Should().Be(0);
    }

    [Fact]
    public async Task parallel_for_each_async_should_throw_when_source_is_null()
    {
        // given
        IEnumerable<int> source = null!;

        // when
        var act = () => source.ParallelForEachAsync(_ => Task.CompletedTask, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task parallel_for_each_async_should_throw_when_action_is_null()
    {
        // given
        var source = new[] { 1 };

        // when
        var act = () => source.ParallelForEachAsync((Func<int, Task>)null!, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
