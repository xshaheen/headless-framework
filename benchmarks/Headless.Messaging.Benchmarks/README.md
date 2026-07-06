# Headless Messaging Benchmarks

Micro-benchmarks for the per-message **publish/consume dispatch** paths in `Headless.Messaging.Core`. The suite
isolates the residual per-dispatch allocation costs identified by the hot-path performance audit
(`.context/docs/perf-audit-22-06-2026.md`) so candidate optimizations can be validated against a baseline.

The benchmarks drive the publish/consume middleware pipelines directly (synchronous, in-process, no transport
or background processor), so they measure exactly the dispatch plumbing — not transport or storage I/O.

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
  middleware count (0 / 1 / 5). Each call exercises the residual consume dispatch costs:
  - **F-2** middleware resolution (`_ResolveMiddleware`): `GetServices` + LINQ/array materialization per call.
  - **F-14** header copy: `new MessageHeader(originHeaders)` clones the header dictionary per call.
- **`PublishDispatchBenchmarks`** — full per-publish path via `ExecuteAsync`, parameterized by middleware count
  (0 / 1 / 5) and header count (0 / 8). This isolates publish-context creation, header snapshotting, middleware
  service resolution, and delegate-chain construction without transport/storage work.
- **`MessageHeaderBenchmarks`** — isolates **F-14**: `new MessageHeader(headers)` parameterized by header
  count (4 / 8 / 16), so the copy cost can be read independently of the rest of the dispatch path.
