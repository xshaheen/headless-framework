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

Three more probes print exact allocations and counts next to a plain `DbContext` that maps the same CLR types:

- `--save-cost-probe [--filter <scenario>]`: bytes per save, change-detection passes, commands, and transactions for
  plain, stamped, tenant-guarded, audited, and aggregate entities over no-op, update, and insert workloads.
  `HEADLESS_PROBE_ITERS` sets the iteration count (default 3000).
- `--context-probe`: context and options resolution per scope, generated Guid keys, tracked queries, the `File` and
  `Image` column comparers, and query compilations for `CountPerMonthAsync`.
- `--transaction-probe`: commands and transactions of one audited insert and update next to plain EF Core saving the
  same entity row and audit row in one `SaveChanges`. Set `HEADLESS_BENCH_POSTGRES` to a PostgreSQL connection string
  to run it there instead of in-memory SQLite.
- `--stamp-check` and `--audit-row-cost`: whether the stored concurrency stamp matches the entity after an update, and
  the cost per audit row of the tracked write next to one raw multi-row `INSERT` of the same rows.

Bytes are exact (`GC.GetTotalAllocatedBytes(precise: true)` or the current thread's counter, minimum of five batches);
microseconds are noisy on a shared machine.
