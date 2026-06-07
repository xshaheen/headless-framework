# Headless.Coordination.Core.Database

Provides the shared relational substrate used by native SQL coordination providers.

## Problem Solved

Centralizes relational membership schema names and provider hooks so PostgreSQL and SQL Server providers do not drift.

## Key Features

- Descriptor, liveness, and node-generation table vocabulary.
- Base store operation order for relational providers.
- Initializer contract for race-safe DDL.

## Design Notes

Provider SQL and clock expressions stay in native provider packages. This package does not choose `clock_timestamp()` or `SYSUTCDATETIME()`.

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
