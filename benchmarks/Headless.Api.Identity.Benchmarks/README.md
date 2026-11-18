# Headless API identity benchmarks

Measures one authenticated request through the real `Basic` and `ApiKey` handlers, ASP.NET Core Identity's sign-in
manager, password hasher, and claims factory, with the credential cache off (the default) and on.

The user store is in memory and does no I/O, so the uncached rows are the CPU floor. Against a database-backed store,
every uncached request also pays its round trips: the user or key lookup, the role and claim queries, and a write when
Identity resets the failed-attempt count. A cached hit makes none of them.

```bash
make bench BENCH_AREA=Api.Identity
make bench BENCH_AREA=Api.Identity BENCH_FILTER='*' BENCH_JOB=Default
```
