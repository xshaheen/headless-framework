# Headless.Tus.DistributedLocks

Distributed lock-based TUS file lock provider, using `Headless.DistributedLocks` to prevent concurrent PATCH corruption across multiple application instances.

## Why use this package

The TUS protocol allows only one concurrent PATCH per file. On single-instance deployments, `tusdotnet`'s default in-process locking suffices. On multi-instance deployments (load-balanced or Kubernetes pods), each instance has its own in-process lock table, so two nodes can simultaneously PATCH the same file, producing interleaved blocks and corrupted uploads. `DistributedLockTusLockProvider` uses the framework's `IDistributedLock` to coordinate across nodes.

## Install

```bash
dotnet add package Headless.Tus.DistributedLocks
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [TUS (Resumable Uploads) guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/tus.md#headlesstusdistributedlocks)
