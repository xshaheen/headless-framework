# Headless.CommitCoordination.EntityFramework

## Problem Solved

Provides EF Core commit coordination registration points.

## Key Features

- `EntityFrameworkCommitSignalSource`.
- DI extension `AddEntityFrameworkCommitCoordination()`.

## Installation

```bash
dotnet add package Headless.CommitCoordination.EntityFramework
```

## Quick Start

```csharp
services.AddEntityFrameworkCommitCoordination();
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.EntityFrameworkCore.Relational`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

Registers core commit coordination services, `EntityFrameworkCommitSignalSource`, `ICommitSignalSource`, and the EF transaction interceptor.
