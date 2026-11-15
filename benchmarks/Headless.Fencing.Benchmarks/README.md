# Headless Fencing benchmarks

Measures the per-request liveness check at 32 concurrent callers on PostgreSQL and SQL Server:

- `GetStatusAsync(lease)`: one statement on its own pooled connection, no transaction.
- `GetStatusAsync(lease, connection)`: the same statement on the caller's open connection.
- `GetCurrentAsync(kind, resource)`: the snapshot read on its own pooled connection.
- Baseline: a plain primary-key `SELECT` of the same row on the caller's connection, bound with the parameter types
  the lease store binds.

It also measures the coordination membership reads every node runs on each heartbeat tick, at the same 32 callers
over a 16-node cluster whose nodes all stay alive for the run:

- `ReadLivenessAsync()`: the liveness snapshot of every node.
- `ReadNodeLivenessAsync(identity)`: one node's state.
- `ReadLiveNodesAsync()`: the live set.

The lease table holds 256 live leases and each caller reads its own, so callers never contend on one row. Every read
checks that it saw a live lease, so a run cannot measure a short-circuit. One operation is one read; the `Op/s` column
is calls per second across all callers.

```bash
make bench BENCH_AREA=Fencing
make bench BENCH_AREA=Fencing BENCH_FILTER='*LivenessRead*'
make bench BENCH_AREA=Fencing BENCH_FILTER='*' BENCH_JOB=Default
```

Each benchmark process starts its own container (the images the integration fixtures pin) and creates a throwaway
database in it. To run against a server you already have, set `HEADLESS_BENCH_POSTGRES` or `HEADLESS_BENCH_SQLSERVER`
to an admin connection string; the run creates its own database there and drops it afterwards.

On an ARM64 host SQL Server runs under x86_64 emulation, so read its figures relative to the baseline, not as
absolute throughput.
