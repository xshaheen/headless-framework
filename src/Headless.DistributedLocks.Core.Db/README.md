# Headless.DistributedLocks.Core.Db

## Problem Solved

Lets database providers map session-scoped or transaction-scoped lock primitives onto the standard distributed-lock abstractions without adding ADO.NET-specific machinery to Redis or cache providers.

## Key Features

- `IConnectionScopedLockStorage` for non-blocking session-held lock acquisition and release.
- `ConnectionScopedDistributedLockProvider` implements `IDistributedLockProvider` over connection-scoped storage.
- `ConnectionScopedReaderWriterLockProvider` implements `IDistributedReaderWriterLockProvider` over shared/exclusive storage.
- `IFencingTokenSource` lets database providers stamp mutex handles with durable sequence-backed fencing tokens.
- `IReleaseSignal` provides the wake-up seam for provider push notifications plus polling fallback.

## Design Notes

- Connection-scoped locks have no TTL. `RenewAsync(...)` is a no-op success, `GetExpirationAsync(...)` returns `null`, and handle loss is tied to the storage connection's loss token.
- Reader-writer locks do not issue fencing tokens; `FencingToken` is `null` for read and write handles.

## Installation

```bash
dotnet add package Headless.DistributedLocks.Core.Db
```

## Quick Start

Use a concrete provider such as `Headless.DistributedLocks.Postgres`; application code normally does not register `Core.Db` directly.

## Configuration

None directly. Concrete providers own options and storage configuration.

## Dependencies

- `Headless.DistributedLocks.Abstractions`
- `Headless.DistributedLocks.Core`
- `Headless.Core`
- `Headless.Hosting`

## Side Effects

None by itself. Concrete providers register the public lock providers.
