# Headless.DistributedLocks.InMemory

In-process storage and setup helpers for distributed-lock abstractions.

## Problem Solved

Provides a no-infrastructure backend for code that depends on `IDistributedLockProvider`, `IDistributedReaderWriterLockProvider`, or `IDistributedSemaphoreProvider` in tests, local development, and single-instance applications.

## Key Features

- `InMemoryDistributedLockStorage` implements `IDistributedLockStorage`.
- `InMemoryDistributedReaderWriterLockStorage` implements `IDistributedReaderWriterLockStorage`.
- `InMemoryDistributedSemaphoreStorage` implements `IDistributedSemaphoreStorage`.
- `AddInMemoryDistributedLock(...)` registers an in-process mutex provider.
- `AddInMemoryDistributedReaderWriterLock(...)` registers an in-process reader-writer lock provider.
- `AddInMemoryDistributedSemaphore(...)` registers an in-process semaphore provider.
- Uses injected `TimeProvider` for deterministic TTL behavior.

## Design Notes

This package is process-local. It does not coordinate across app instances, machines, containers, or processes. Use it when one process owns all contenders, or when tests need a real provider without Redis. Fencing tokens are monotonic inside the process lifetime only.

Reader-writer lock ids must not contain `:` because that character is reserved for the writer-waiting marker suffix; ids containing it are rejected.

## Installation

```bash
dotnet add package Headless.DistributedLocks.InMemory
```

## Quick Start

```csharp
builder.Services.AddInMemoryDistributedLock(options =>
{
    options.KeyPrefix = "distributed-lock:";
});

builder.Services.AddInMemoryDistributedReaderWriterLock(options =>
{
    options.KeyPrefix = "distributed-lock:";
});

builder.Services.AddInMemoryDistributedSemaphore(options =>
{
    options.KeyPrefix = "distributed-lock:";
});
```

## Configuration

No InMemory-specific options. Configure `DistributedLockOptions`.

Reader-writer and semaphore TTL checks use the registered `TimeProvider`, so tests can register a fake clock and advance leases deterministically. `HandleLostToken` is `CancellationToken.None` unless monitoring is enabled through `DistributedLockAcquireOptions`.

## Dependencies

- `Headless.DistributedLocks.Core`

## Side Effects

- Registers `IDistributedLockProvider` through `Headless.DistributedLocks.Core`.
- Registers `IDistributedReaderWriterLockProvider` through `Headless.DistributedLocks.Core` when `AddInMemoryDistributedReaderWriterLock(...)` is called.
- Registers `IDistributedSemaphoreProvider` through `Headless.DistributedLocks.Core` when `AddInMemoryDistributedSemaphore(...)` is called.
- Registers process-local singleton storage instances for the selected lock primitive.
