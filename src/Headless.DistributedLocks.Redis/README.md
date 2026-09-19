# Headless.DistributedLocks.Redis

Redis-backed storage and setup helpers for distributed locks, reader-writer locks, and semaphores.

## Why use this package

Stores lock records directly in Redis with atomic acquire, replace, release, reader-writer transitions, semaphore slots, and fencing-token issuance.

## Install

```bash
dotnet add package Headless.DistributedLocks.Redis
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Distributed Locks guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/distributed-locks.md#headlessdistributedlocksredis)
