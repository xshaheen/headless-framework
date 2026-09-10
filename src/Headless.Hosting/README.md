# Headless.Hosting

Core hosting utilities and extensions for ASP.NET Core applications.

## Problem Solved

Provides essential DI extensions, configuration helpers, options validation, and seeder infrastructure to reduce boilerplate in application startup and configuration.

## Key Features

- DI extensions: `AddIf`, `AddIfElse`, `Decorate`, `TryDecorate`, `AddOrReplace*`, `AddOrReplaceFallbackSingleton`, `Unregister<T>`
- Required-service declarations (`RequireRegisteredService<T>`) that fail the host at startup instead of at first use
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

### Required Services

The abstraction-plus-provider split lets a package register cleanly against a contract whose only implementation ships in a *provider* package the host must choose — `Headless.Settings.Core` consumes `ICache<SettingValueCacheItem>` while referencing only `Headless.Caching.Abstractions`, for example. Without a declared prerequisite such a host starts green and throws on the first request that touches the feature.

```csharp
services.RequireRegisteredService<ICache<SettingValueCacheItem>>(
    requiredBy: "Headless settings value caching",
    remedy: "Call AddHeadlessCaching(...) with a provider (UseInMemory / UseRedis / UseHybrid)."
);
```

- **Checked at startup, not at declaration.** The requirement is usually satisfied by a sibling `Add…` call that has not run yet, so inspecting the collection at declaration time would reject valid registration orders. The check runs as an `IHostedLifecycleService.StartingAsync`, ahead of every hosted service's `StartAsync`, so a broken host never lets background workers or consumers start under an assumption the container cannot honour.
- **Probed, never resolved.** It asks `IServiceProviderIsService` rather than resolving the contract, so validation never constructs the service under test (a Redis-backed cache would reach into its connection options and turn a provider misconfiguration into an opaque failure from the guard). MS.DI's probe answers a *constructed* generic from an *open*-generic registration, so `ICache<Foo>` reports present when only `typeof(ICache<>)` was registered — which is exactly how the caching providers register. A container that does not expose the probe falls back to a null-returning resolve.
- **Aggregated.** Requirements from every feature in the host are collected and reported in one `MissingRequiredServiceException`, each line naming its `requiredBy` and `remedy`. A host missing one shared provider sees every affected feature at once instead of one failure per restart. Identical declarations collapse to a single line, and the startup check itself is registered once no matter how many features declare requirements.

`Headless.MultiTenancy`, `Headless.Settings.Core`, `Headless.Permissions.Core`, `Headless.Features.Core`, and `Headless.Api.Idempotency` all use this to require a caching provider.

## Configuration

No configuration required.

## Dependencies

- `Headless.FluentValidation`
- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Options`

## Side Effects

None directly. Utilities for managing service registration.
