# Headless.Coordination.Core.Database

Provides the shared relational substrate used by native SQL coordination providers.

## Problem Solved

Provides relational membership operation hooks so PostgreSQL and SQL Server providers share behavior without sharing physical table names.

## Key Features

- Base store operation order for relational providers.
- Provider-owned physical identifiers: PostgreSQL uses snake_case; SQL Server uses PascalCase.
- Initializer contract for race-safe DDL.

## Design Notes

Provider SQL, clock expressions, and physical identifiers stay in native provider packages. This package does not choose `clock_timestamp()`, `SYSUTCDATETIME()`, snake_case, or PascalCase.

## Installation

```bash
dotnet add package Headless.Coordination.Core.Database
```

## Quick Start

This package is used by provider packages; applications normally install PostgreSQL or SQL Server providers directly.

## Configuration

None.

## Dependencies

- `Headless.Coordination.Core`
- `Headless.Hosting`

## Side Effects

None.
