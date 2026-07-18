# Headless Blobs benchmarks

Measures allocation and throughput for reflection-based and source-generated typed JSON blob reads at 256 B,
4 KiB, 64 KiB, and 1 MiB.

Run in Release mode after adding the project to the solution:

```bash
dotnet run -c Release --project benchmarks/Headless.Blobs.Benchmarks -- --filter '*BlobJsonBenchmarks*'
```

The string methods reproduce the previous UTF-16 intermediate. The stream methods exercise the production typed
read overloads. Compare raw artifacts from independent baseline and candidate launches.
