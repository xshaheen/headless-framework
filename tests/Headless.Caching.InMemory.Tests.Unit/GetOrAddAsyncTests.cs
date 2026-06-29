// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>Stampede behavior of <c>GetOrAddAsync</c> through the public <see cref="ICache"/> surface.</summary>
public sealed class GetOrAddAsyncTests : TestBase
{
    [Fact]
    public async Task should_invoke_factory_once_for_high_concurrency_cold_stampede()
    {
        // given — a REAL time provider (no fake-time orchestration) and a briefly-gated factory
        using var cache = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        var key = Faker.Random.AlphaNumeric(10);
        var factoryCalls = 0;
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) };

        async ValueTask<string?> factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            return await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // when — ~300 concurrent cold callers stampede one key through the public surface
        var tasks = Enumerable
            .Range(0, 300)
            .Select(_ => Task.Run(() => cache.GetOrAddAsync(key, factory, options, AbortToken).AsTask()))
            .ToArray();

        await Task.Delay(100, AbortToken); // let the pack pile up on the keyed lock before the factory completes
        gate.SetResult("fresh");
        var results = await Task.WhenAll(tasks);

        // then — single flight: exactly one factory call; every caller got the fresh value
        factoryCalls.Should().Be(1);
        results.Should().HaveCount(300);
        results.Should().OnlyContain(result => result.HasValue && result.Value == "fresh");
    }
}
