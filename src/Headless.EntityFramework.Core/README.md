# Headless.EntityFramework.Core

## Problem Solved

Provides provider-neutral Entity Framework Core primitives that feature packages can reuse without depending on the full `Headless.EntityFramework` context, save pipeline, auditing, tenancy, or hosting integration.

## Key Features

- Provider-neutral converters and comparers for dates, JSON-backed values, locales, extra properties, and Headless primitives.
- Money and phone model configuration plus pagination, ordering, data-grid, date aggregation, entity lookup, and asynchronous lookup helpers.
- Generic model/configuration helpers that do not require `HeadlessDbContext` or runtime policy.
- `DateTimeKind.Unspecified` is treated as an already-UTC relational value and stamped without shifting its clock value.

## Design Notes

- This package is intentionally independent of `HeadlessDbContext`. Storage feature packages can consume its EF primitives without inheriting application-level ORM behavior.
- `NormalizeDateTimeValueConverter` is the single UTC-normalization API; Core does not expose a parallel converter family.
- The package has no database-provider, hosting, interception, tenancy, auditing, or save-pipeline dependency.

## Installation

```bash
dotnet add package Headless.EntityFramework.Core
```

## Quick Start

```csharp
using Headless.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;

public sealed class ScheduledWork
{
    public DateTime DateCreated { get; set; }
    public DateTime? DateCompleted { get; set; }
}

modelBuilder.Entity<ScheduledWork>(entity =>
{
    entity.Property(x => x.DateCreated).HasConversion(new NormalizeDateTimeValueConverter());
    entity.Property(x => x.DateCompleted).HasConversion(new NullableNormalizeDateTimeValueConverter());
});

var page = await dbContext.Set<ScheduledWork>().ToIndexPageAsync(0, 25, cancellationToken);
```

## Configuration

Converters accept optional EF Core mapping hints. Query and model helpers are opt-in and require no Headless runtime registration.

## Dependencies

- Headless foundational primitives, checks, domain contracts, extensions, and JSON serialization
- `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational`

## Side Effects

None. The package performs no dependency-injection registration.
