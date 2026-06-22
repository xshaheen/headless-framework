# Headless Messaging Benchmarks

Micro-benchmarks for the per-message **consume dispatch** path in `Headless.Messaging.Core`. The suite
isolates the residual per-dispatch allocation costs identified by the hot-path performance audit
(`.context/docs/perf-audit-22-06-2026.md`) so candidate optimizations can be validated against a baseline.

The benchmarks drive `ConsumeMiddlewarePipeline.ExecuteAsync` directly (synchronous, in-process, no transport
or background processor), so they measure exactly the dispatch plumbing — not transport or storage I/O. The
project is granted `InternalsVisibleTo` by `Headless.Messaging.Core` to reach the internal pipeline.

## Run

```bash
# All benchmarks (full job — final comparison runs)
dotnet run -c Release --project benchmarks/Headless.Messaging.Benchmarks -- --filter '*'

# Fast smoke check (reduced job)
dotnet run -c Release --project benchmarks/Headless.Messaging.Benchmarks -- --job short --filter '*'

# Dry run (correctness only, not timing)
dotnet run -c Release --project benchmarks/Headless.Messaging.Benchmarks -- -j Dry --filter '*'
```

BenchmarkDotNet writes Markdown and HTML artifacts under `artifacts/benchmark/messaging`. `MemoryDiagnoser` is
always enabled — the **Allocated** column is the headline metric for these allocation-reduction findings.

## Lanes

- **`ConsumeDispatchBenchmarks`** — full per-dispatch path via `ExecuteAsync`, parameterized by registered
  middleware count (0 / 1 / 5). Each call exercises three audit findings together:
  - **F-2** middleware resolution (`_ResolveMiddleware`): `MakeGenericType` + `GetServices` + LINQ per call.
  - **F-3** reflection dispatch fallback (`_DispatchAsync`): `MakeGenericMethod(...).Invoke(...)` per call
    (the descriptor carries no `HandlerId`, so the runtime-invoker fast path is skipped).
  - **F-14** header copy: `new MessageHeader(originHeaders)` clones the header dictionary per call.
- **`MessageHeaderBenchmarks`** — isolates **F-14**: `new MessageHeader(headers)` parameterized by header
  count (4 / 8 / 16), so the copy cost can be read independently of the rest of the dispatch path.
