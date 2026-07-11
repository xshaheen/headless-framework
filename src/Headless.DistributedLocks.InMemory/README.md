# Headless.DistributedLocks.InMemory

In-process storage and setup helpers for distributed-lock abstractions.

## Problem Solved

Provides a no-infrastructure backend for code that depends on `IDistributedLock`, `IDistributedReadWriteLock`, or `IDistributedSemaphoreProvider` in tests, local development, and single-instance applications.

## Key Features

- `InMemoryDistributedLockStorage` implements `IDistributedLockStorage`.
- `InMemoryDistributedReadWriteLockStorage` implements `IDistributedReadWriteLockStorage`.
- `InMemoryDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `UseInMemory()` registers in-process mutex, reader-writer lock, and semaphore providers through `AddHeadlessDistributedLocks(...)`.
- Uses injected `TimeProvider` for deterministic TTL behavior.
- Mutex compare-and-swap preserves the existing absolute expiration when `ReplaceIfEqualAsync(..., newTtl: null)` is used.

## Design Notes

This package is process-local. It does not coordinate across app instances, machines, containers, or processes. Use it when one process owns all contenders, or when tests need a real provider without Redis. Fencing tokens are monotonic inside the process lifetime only.

Reader-writer lease ids must not contain `:` because that character is reserved for the writer-waiting marker suffix; ids containing it are rejected.

## Installation

```bash
dotnet add package Headless.DistributedLocks.InMemory
```

## Quick Start

```csharp
builder.Services.AddHeadlessDistributedLocks(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.KeyPrefix = "distributed-lock:";
    });

    setup.UseInMemory();
});
```

## Configuration

No InMemory-specific options. Configure `DistributedLockOptions`.

Reader-writer and semaphore TTL checks use the registered `TimeProvider`, so tests can register a fake clock and advance leases deterministically. `LostToken` is `CancellationToken.None` unless monitoring is enabled through `DistributedLockAcquireOptions`.

## Dependencies

- `Headless.DistributedLocks.Core`

## Side Effects

- Registers `IDistributedLock`, `IDistributedReadWriteLock`, and `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core`.
- Registers process-local singleton storage instances for all three primitives.
