# Headless Caching Benchmarks

Benchmark suite for comparing Headless `ICache` providers with FusionCache, Foundatio, and Microsoft `IDistributedCache`.

## Run

```bash
dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- --filter '*CommonCacheBenchmarks*'
```

By default the suite runs only in-process providers so local validation stays deterministic.

Set `HEADLESS_CACHE_BENCHMARK_REDIS` to opt into Redis-backed FusionCache and Microsoft `IDistributedCache` scenarios:

```bash
HEADLESS_CACHE_BENCHMARK_REDIS=localhost:6379 dotnet run -c Release --project benchmarks/Headless.Caching.Benchmarks -- --filter '*Redis*'
```

BenchmarkDotNet writes Markdown and HTML artifacts under `BenchmarkDotNet.Artifacts`.

## Lanes

- Common lane: set, hot get, miss get, remove across every registered provider.
- Factory lane: atomic `GetOrAdd` providers only.
- Feature lane: providers with hybrid, fail-safe, or eager-refresh semantics only.
