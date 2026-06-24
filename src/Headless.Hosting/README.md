# Headless.Hosting

Core hosting utilities and extensions for ASP.NET Core applications.

## Problem Solved

Provides essential DI extensions, configuration helpers, options validation, and seeder infrastructure to reduce boilerplate in application startup and configuration.

## Key Features

- DI extensions: `AddIf`, `AddIfElse`, `Decorate`, `TryDecorate`, `AddOrReplace*`, `AddOrReplaceFallbackSingleton`, `Unregister<T>`
- Options validation with FluentValidation
- Configuration binding extensions
- Environment detection extensions
- Database seeder infrastructure (`ISeeder`, ordered by `[SeederPriority]`)
- Keyed services helpers
- Hosted service management

## Installation

```bash
dotnet add package Headless.Hosting
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Conditional registration
builder.Services.AddIf(
    builder.Environment.IsDevelopment(),
    s => s.AddHeadlessEmails(setup => setup.UseDevelopment("emails.txt"))
);

// Options with FluentValidation
builder.Services.Configure<MyOptions, MyOptionsValidator>(builder.Configuration.GetSection("MySection"));

// Replace existing service
builder.Services.AddOrReplaceSingleton<IMyService, BetterMyService>();

// Decorate existing services while preserving their original lifetime
builder.Services.Decorate<IMyService, AuditedMyService>();
```

### Conditional Service Registration

```csharp
services.AddIf(condition, s => s.AddSingleton<IService, Impl>());
services.AddIfElse(condition, ifAction, elseAction);
```

### Options with Validation

```csharp
services.Configure<AppOptions, AppOptionsValidator>(configuration.GetSection("App"));
```

### Database Seeders

Implement `ISeeder` and register with `AddSeeder<T>()`. All seeders run via a single
`SeedAsync()` ascending by `[SeederPriority(n)]` (default `0`; lower runs first — EF migrations
use `int.MinValue` so they run before data seeders).

```csharp
[SeederPriority(1)]
public sealed class UserSeeder : ISeeder
{
    public ValueTask SeedAsync(CancellationToken ct = default) { /* seed users */ return ValueTask.CompletedTask; }
}

// Registration
builder.Services.AddSeeder<UserSeeder>();
builder.Services.AddDbMigrationSeeder<AppDbContext>(); // runs first (SeederPriority int.MinValue)

// In startup — runs all seeders in priority order
await app.Services.SeedAsync();
```

### Service Replacement

```csharp
services.AddOrReplaceScoped<IService, NewImpl>();
services.AddOrReplaceSingleton<IService>(sp => new Impl(sp.GetRequired<IDep>()));
services.AddOrReplaceFallbackSingleton<IService, NullService, DefaultService>();
```

### Service Decoration

```csharp
services.AddSingleton<IService, Service>();
services.Decorate<IService, AuditedService>();

services.Decorate<IService>(
    (inner, serviceProvider) => new AuditedService(inner, serviceProvider.GetRequiredService<ILogger<AuditedService>>())
);
```

`Decorate` wraps all existing unkeyed registrations for the service type and preserves each original lifetime. Use `TryDecorate` when the service may not be registered.

## Configuration

No configuration required.

## Dependencies

- `Headless.FluentValidation`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Options`

## Side Effects

None directly. Utilities for managing service registration.
