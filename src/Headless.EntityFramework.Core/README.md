# Headless.EntityFramework.Core

## Problem Solved

Provides provider-neutral Entity Framework Core primitives that feature packages can reuse without depending on the full `Headless.EntityFramework` context, save pipeline, auditing, tenancy, or hosting integration.

## Key Features

- `UtcDateTimeValueConverter` normalizes `DateTime` values to UTC on database writes and reads.
- `NullableUtcDateTimeValueConverter` provides the same normalization while preserving `null` values.
- `DateTimeKind.Unspecified` is treated as an already-UTC relational value and stamped without shifting its clock value.

## Design Notes

- This package is intentionally independent of `HeadlessDbContext`. Storage feature packages can consume its EF primitives without inheriting application-level ORM behavior.
- UTC normalization delegates to `DateTime.NormalizeToUtc()` from `Headless.Extensions`, keeping the framework's handling of `Local`, `Utc`, and `Unspecified` values consistent.

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
    entity.Property(x => x.DateCreated).HasConversion(new UtcDateTimeValueConverter());
    entity.Property(x => x.DateCompleted).HasConversion(new NullableUtcDateTimeValueConverter());
});
```

## Configuration

Both converters accept optional EF Core `ConverterMappingHints`. They do not require dependency injection or runtime clock services because UTC normalization is deterministic.

## Dependencies

- `Headless.Extensions`
- `Microsoft.EntityFrameworkCore`

## Side Effects

None. The package performs no dependency-injection registration and applies converters only where consumers configure them.
