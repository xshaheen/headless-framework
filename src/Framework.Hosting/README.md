# Framework.Hosting

Core hosting utilities and extensions for ASP.NET Core applications.

## Problem Solved

Provides essential DI extensions, configuration helpers, options validation, and seeder infrastructure to reduce boilerplate in application startup and configuration.

## Key Features

- DI extensions: `AddIf`, `AddIfElse`, `AddOrReplace*`, `Unregister<T>`
- Options validation with FluentValidation
- Configuration binding extensions
- Environment detection extensions
- Database seeder infrastructure (`ISeeder`, `IPreSeeder`)
- Keyed services helpers
- Hosted service management

## Installation

```bash
dotnet add package Framework.Hosting
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Conditional registration
builder.Services.AddIf(
    builder.Environment.IsDevelopment(),
    s => s.AddDevEmailSender("emails.txt")
);

// Options with FluentValidation
builder.Services.AddOptionsWithFluentValidation<MyOptions, MyOptionsValidator>("MySection");

// Replace existing service
builder.Services.AddOrReplaceSingleton<IMyService, BetterMyService>();
```

## Usage

### Conditional Service Registration

```csharp
services.AddIf(condition, s => s.AddSingleton<IService, Impl>());
services.AddIfElse(condition, ifAction, elseAction);
```

### Options with Validation

```csharp
services.AddOptionsWithFluentValidation<AppOptions, AppOptionsValidator>("App");
```

### Database Seeders

```csharp
public class UserSeeder : ISeeder
{
    [SeederPriority(1)]
    public async Task SeedAsync(CancellationToken ct) { /* ... */ }
}

// In startup
await app.Services.RunSeedersAsync();
```

### Service Replacement

```csharp
services.AddOrReplaceScoped<IService, NewImpl>();
services.AddOrReplaceSingleton<IService>(sp => new Impl(sp.GetRequired<IDep>()));
```

## Configuration

No configuration required.

## Dependencies

- `Framework.Checks`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Options`

## Side Effects

None directly. Utilities for managing service registration.
