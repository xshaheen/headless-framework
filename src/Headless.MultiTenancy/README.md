# Headless.MultiTenancy

Root tenancy configuration for Headless applications.

## Problem Solved

Provides one composition surface for tenant posture across Headless packages while keeping each package in charge of its own behavior.

## Key Features

- `AddHeadlessTenancy(...)` root configuration entry point
- `TenantPostureManifest` for non-PII seam posture diagnostics
- `IHeadlessTenancyValidator` startup validation hook
- Seam-owned fluent extensions from installed packages:
  - `Headless.Api`: `.Http(http => http.ResolveFromClaims())`
  - `Headless.Mediator`: `.Mediator(mediator => mediator.RequireTenant())`
  - `Headless.Messaging.Core`: `.Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())`
  - `Headless.Orm.EntityFramework`: `.EntityFramework(ef => ef.GuardTenantWrites())`

## Installation

```bash
dotnet add package Headless.MultiTenancy
```

Most applications receive this package transitively through the seam packages that contribute tenancy extensions.

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadlessInfrastructure();
builder.AddHeadlessTenancy(tenancy => tenancy
    .Http(http => http.ResolveFromClaims())
    .Mediator(mediator => mediator.RequireTenant())
    .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
    .EntityFramework(ef => ef.GuardTenantWrites()));

var app = builder.Build();

app.UseHeadlessDefaults();
app.UseAuthentication();
app.UseHeadlessTenancy();
app.UseAuthorization();
```

`UseHeadlessTenancy()` belongs after application-owned authentication and before application-owned authorization. It does not call either middleware internally.

## Configuration

`Headless.MultiTenancy` does not resolve tenants, enforce Mediator boundaries, propagate messages, or guard EF writes by itself. It owns the root builder, shared manifest, and validator contracts only. The installed seam packages add the actual fluent methods and behavior.

`TenantPostureManifest` records seam names, posture status, capability labels, and runtime markers. Values are intentionally non-PII so diagnostics can be emitted safely.

## Dependencies

- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Hosting.Abstractions`

## Side Effects

- Registers a singleton `TenantPostureManifest`
- Registers `HeadlessTenancyStartupValidator` as an `IHostedService`
