# Headless.CommitCoordination.InMemory

## Problem Solved

Provides an explicit in-process signal source for tests and owner-driven flows.

## Key Features

- `InMemoryCommitSignalSource`.
- DI extension `AddInMemoryCommitCoordination()`.

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

Registers core commit coordination services and `ICommitSignalSource`.
