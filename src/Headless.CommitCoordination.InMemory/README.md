# Headless.CommitCoordination.InMemory

## Problem Solved

Provides an explicit in-process signal source for tests, local development, and single-instance owner-driven flows.

## Key Features

- `InMemoryCommitSignalSource`.
- DI extension `AddInMemoryCommitCoordination()`.

## Design Notes

This package is process-local. It does not coordinate commit signals across app instances, machines, containers, or processes. Use it only when one process owns all commit observers, or when tests need a real signal source without an external coordinator.

For multi-process or distributed commit coordination, use a durable/provider-backed coordination package instead of this in-memory implementation.

## Installation

```bash
dotnet add package Headless.CommitCoordination.InMemory
```

## Quick Start

```csharp
services.AddInMemoryCommitCoordination();
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

Registers core commit coordination services, `InMemoryCommitSignalSource`, and `ICommitSignalSource`.
