# Headless.DistributedLocks.Messaging

Bridge package that pushes distributed-lock release wake-ups to every host over the Headless messaging bus.

## Why use this package

`Headless.DistributedLocks` defines the `IDistributedLockReleaseSignal` seam but ships only a default that sends nothing, so it carries no messaging dependency and waiters wake on their backoff poll. This package supplies the messaging implementation: each confirmed release publishes a `DistributedLockReleased` message, and an every-instance consumer on each host wakes the local lock, semaphore, and reader-writer waiters at once.

## Install

```bash
dotnet add package Headless.DistributedLocks.Messaging
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Distributed locks guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/distributed-locks.md#headlessdistributedlocksmessaging)
