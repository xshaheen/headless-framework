// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Caching.Benchmarks.Scenarios;

/// <summary>
/// Measures the Headless in-memory set paging path directly. The benchmark isolates the page read from provider
/// comparison overhead so allocation changes in <see cref="InMemoryCache.GetSetAsync{T}"/> are easy to inspect.
/// </summary>
[MemoryDiagnoser]
public sealed class InMemorySetPagingBenchmarks : IDisposable
{
    private readonly TimeSpan _expiration = TimeSpan.FromMinutes(5);
    private InMemoryCache? _cache;
    private string _key = "";

    [Params(100, 10_000)]
    public int SetSize { get; set; }

    [Params(1, 50)]
    public int PageIndex { get; set; }

    public int PageSize { get; set; } = 50;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _cache = new InMemoryCache(TimeProvider.System, new InMemoryCacheOptions());
        _key = string.Create(CultureInfo.InvariantCulture, $"set:{SetSize}:{PageIndex}:{Guid.NewGuid():N}");
        var values = Enumerable
            .Range(0, SetSize)
            .Select(static i => string.Create(CultureInfo.InvariantCulture, $"item-{i}"));
        await _cache.SetAddAsync(_key, values, _expiration, CancellationToken.None).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public void GlobalCleanup() => Dispose();

    public void Dispose()
    {
        _cache?.Dispose();
        _cache = null;
    }

    [Benchmark]
    public async ValueTask<CacheValue<ICollection<string>>> GetStringSetPageAsync()
    {
        return await Cache.GetSetAsync<string>(_key, PageIndex, PageSize, CancellationToken.None).ConfigureAwait(false);
    }

    private InMemoryCache Cache =>
        _cache ?? throw new InvalidOperationException("Benchmark cache was not initialized.");
}
