# Headless.CommitCoordination.PostgreSql

## Problem Solved

Provides PostgreSQL commit coordination registration points for inline framework-owned transaction flows.

## Key Features

- `PostgreSqlCommitSignalSource`.
- DI extension `AddPostgreSqlCommitCoordination()`.

## Installation

```bash
dotnet add package Headless.CommitCoordination.PostgreSql
```

## Quick Start

```csharp
services.AddPostgreSqlCommitCoordination();
```

## Configuration

None.

## Dependencies

- `Headless.CommitCoordination.Core`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Npgsql`

## Side Effects

Registers core commit coordination services and `PostgreSqlCommitSignalSource`.
