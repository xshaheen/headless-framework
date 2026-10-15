# Headless EntityFramework benchmarks

Measures the cost of the EF save pipeline: one audited save among 1,000 unchanged tracked entities, and audited
inserts of 1, 10, and 100 rows.

The database is in-memory SQLite, so absolute times exclude network and server cost. Compare the relative cost of the
framework's own work between runs, not production latency.

```bash
make bench BENCH_AREA=EntityFramework
dotnet run -c Release --project benchmarks/Headless.EntityFramework.Benchmarks -- --save-pipeline-probe
```

`--save-pipeline-probe` prints the change-detection passes and database commands per save. The counts are exact, so
they are not a BenchmarkDotNet metric.
