# Headless Caching Benchmarks

Benchmark suite for comparing Headless `ICache` providers with FusionCache, Foundatio, and Microsoft `IDistributedCache`.

## Run

```bash
dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- --filter '*CommonCacheBenchmarks*'
```

By default the suite runs only in-process providers so local validation stays deterministic.
This includes both raw Microsoft `IMemoryCache` and `MemoryDistributedCache` so local object-cache overhead is separated from `IDistributedCache` serialization/contract overhead.

Run standalone memory providers only:

```bash
dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- --filter '*MemoryOnlyCacheBenchmarks*'
```

That command runs a full BenchmarkDotNet job for 64 memory-only cases
(4 providers x 2 payload sizes x 2 key cardinalities x 4 operations), so it is
intended for final comparison runs. For a fast smoke check, pass an explicit
BenchmarkDotNet job:

```bash
dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- -j Dry --filter '*MemoryOnlyCacheBenchmarks.SetThenGetAsync*'
```

Run standalone distributed providers only:

```bash
HEADLESS_CACHE_BENCHMARK_REDIS=localhost:6379 dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- --filter '*DistributedOnlyCacheBenchmarks*'
```

Set `HEADLESS_CACHE_BENCHMARK_REDIS` to opt into Redis-backed FusionCache and Microsoft `IDistributedCache` scenarios:

```bash
HEADLESS_CACHE_BENCHMARK_REDIS=localhost:6379 dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- --filter '*Redis*'
```

BenchmarkDotNet writes Markdown and HTML artifacts under `artifacts/benchmark/caching`.

## Lanes

- Common lane: set, hot get, miss get, remove across every registered provider.
- Memory-only lane: Headless in-memory, FusionCache memory, Foundatio in-memory, and Microsoft `IMemoryCache`.
- Distributed-only lane: Headless Redis, FusionCache Redis with memory skipped, Foundatio Redis, and Microsoft Redis `IDistributedCache`.
- Factory lane: atomic `GetOrAdd` providers only.
- Feature lane: providers with hybrid, fail-safe, or eager-refresh semantics only.
- In-memory set paging lane: direct `InMemoryCache.GetSetAsync<string>` paging over 100 and 10,000 member sets.
