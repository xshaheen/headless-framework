# Headless.Sql.Core

Default implementation package for provider-agnostic SQL helpers.

## Problem Solved

Keeps `Headless.Sql.Abstractions` limited to interfaces while providing a reusable scoped ambient connection implementation for unit-of-work patterns.

## Key Features

- `DefaultSqlCurrentConnection` — concrete thread-safe implementation of `ISqlCurrentConnection` backed by `AsyncLock`.
- Lazily opens one connection per scope and reuses it until disposal.
- Reopens the underlying connection if it is observed closed.

## Installation

```bash
dotnet add package Headless.Sql.Core
```

## Quick Start

```csharp
builder.Services.AddScoped<ISqlCurrentConnection, DefaultSqlCurrentConnection>();
```

## Dependencies

- `Headless.Sql.Abstractions`
- `Nito.AsyncEx`

## Side Effects

None. Register services explicitly.
