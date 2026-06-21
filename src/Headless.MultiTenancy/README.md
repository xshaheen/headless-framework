# Headless.MultiTenancy

## Problem Solved

Provides one composition surface for tenant posture across Headless packages while keeping each package in charge of its own behavior. It owns the root builder, shared manifest, and validator contracts only — it does not resolve tenants, enforce HTTP authorization, propagate messages, or guard EF writes. Seam packages (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.Orm.EntityFramework`) contribute their own fluent extensions on top of this builder.

## Key Features

- `AddHeadlessTenancy(Action<HeadlessTenancyBuilder> configure)` — root configuration entry point; registers the shared manifest and startup validator, then invokes the configure callback.
- `HeadlessTenancyBuilder` — root builder passed to the configure callback. Exposes `ApplicationBuilder`, `Services`, `Manifest`, and `RecordSeam(...)`. Seam packages extend it with their own methods (`.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, `.EntityFramework(...)`).
- `TenantPostureManifest` — thread-safe, singleton, non-PII record of seam posture: status (`TenantPostureStatus`), capability labels, and runtime markers. Diagnostic breadcrumb only; records do not create enforcement.
- `TenantPostureStatus` — enum whose ordinal is posture precedence: `Configured(0) < Propagating(1) < Guarded(2) < Enforcing(3)`. `RecordSeam` always keeps the strongest status across contributions.
- `IHeadlessTenancyValidator` / `HeadlessTenancyDiagnostic` — extension hook for seam packages to emit startup diagnostics. Diagnostics can be `Information`, `Warning`, or startup-blocking `Error`.
- `HeadlessTenancyStartupValidator` — `IHostedLifecycleService` that runs all registered validators in `StartingAsync` before any other hosted service starts; throws `HeadlessTenancyValidationException` (an `InvalidOperationException`) on any `Error` diagnostic.
- `HeadlessTenancyValidationContext` — context record passed to validators: `Services` (the app `IServiceProvider`) + `Manifest`.

## Design Notes

`HeadlessTenancyStartupValidator` is registered as an `IHostedLifecycleService` (not a plain `IHostedService`) so `StartingAsync` runs before any other hosted service's `StartAsync`. This ordering guarantees that a misconfigured posture fails the host before background workers or messaging consumers begin processing under the wrong assumptions.

## Installation

```bash
dotnet add package Headless.MultiTenancy
```

Most applications receive this package transitively through the seam packages that contribute tenancy extensions (`Headless.Api.Core`, `Headless.Messaging.Core`, `Headless.Orm.EntityFramework`). Add it directly only when authoring a custom `IHeadlessTenancyValidator` or a custom seam without pulling in one of those packages.

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless();

builder.AddHeadlessTenancy(tenancy => tenancy
    .Http(http => http.ResolveFromClaims())
    .Authorization(auth => auth.RequireTenant())
    .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
    .EntityFramework(ef => ef.GuardTenantWrites()));

var app = builder.Build();

app.UseHeadless();
app.UseAuthentication();
app.UseHeadlessTenancy();   // after UseAuthentication, before UseAuthorization
app.UseAuthorization();
```

`AddHeadlessTenancy` is the only call owned by this package; the `.Http(...)`, `.Authorization(...)`, `.Messaging(...)`, and `.EntityFramework(...)` extensions are contributed by the respective seam packages once they are installed.

## Configuration

`Headless.MultiTenancy` has no options class and binds no configuration section. The builder is purely a composition surface — every seam package owns its own options and configuration binding.

`TenantPostureManifest` is populated at DI build time by the `configure` callback in `AddHeadlessTenancy`. Seam packages call `builder.RecordSeam(seam, status, capabilities)` to register their posture. `MarkRuntimeApplied(seam, marker)` is called by seam middleware at request time so startup validators can verify middleware placement.

Custom validators implement `IHeadlessTenancyValidator` and register themselves in DI before `AddHeadlessTenancy` is called. `HeadlessTenancyStartupValidator` resolves all `IHeadlessTenancyValidator` registrations from DI via `IEnumerable<IHeadlessTenancyValidator>`.

## Dependencies

- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

## Side Effects

- Registers a singleton `TenantPostureManifest` via `services.AddSingleton(manifest)`.
- Registers `HeadlessTenancyStartupValidator` as `IHostedService` (via `TryAddEnumerable`; safe to call multiple times).
- `AddHeadlessTenancy` also invokes the caller's `configure` callback, which may register additional services from seam packages.
