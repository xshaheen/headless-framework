# Headless Sequences benchmarks

Measures fast-mode `ISequenceGenerator.NextAsync` at 32 concurrent callers on PostgreSQL and SQL Server:

- One key: every caller takes numbers from the same counter, so they queue on its row.
- A key per caller: each caller takes numbers from its own partition, so callers never contend.

Every row exists before the measured calls, so they take the update branch, as a warm counter does. One operation is
one number; the `Op/s` column is numbers per second across all callers.

```bash
make bench BENCH_AREA=Sequences
make bench BENCH_AREA=Sequences BENCH_FILTER='*' BENCH_JOB=Default
```

Each benchmark process starts its own container (the images the integration fixtures pin) and creates a throwaway
database in it. To run against a server you already have, set `HEADLESS_BENCH_POSTGRES` or `HEADLESS_BENCH_SQLSERVER`
to an admin connection string; the run creates its own database there and drops it afterwards.

On an ARM64 host SQL Server runs under x86_64 emulation, so read its figures relative to each other, not as absolute
throughput.
