# Headless Jobs benchmarks

Measures the allocation and throughput cost of typed job-request reads at 256 B, 4 KiB, 64 KiB, and 1 MiB,
with plain UTF-8 and GZip persistence payloads. It also isolates the caller-side array copy in job execution fan-out.

Run in Release mode after adding the project to the solution:

```bash
dotnet run -c Release --project benchmarks/Headless.Jobs.Benchmarks -- --filter '*JobsRequestSerializationBenchmarks*'
```

Compare raw artifacts from independent baseline and candidate launches. The string-intermediate method is the
compatibility baseline; the direct method exercises the production typed-read path.
