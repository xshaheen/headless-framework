# Headless.AuditLog.Core

DI setup package for `Headless.AuditLog`: options validation, setup builders, and the exactly-one-storage-provider registration pipeline.

## Problem Solved

Keeps audit-log contracts provider-neutral while centralizing the public `AddHeadlessAuditLog(...)` setup API and provider extension hook in one Core package.

## Key Features

- `SetupAuditLog.AddHeadlessAuditLog(setup => setup.Use...)` — the single public DI entry point in the `Headless.AuditLog` namespace (add `using Headless.AuditLog;`); requires exactly one storage provider. The options-only registration is `internal` (a funnel the builder overload uses to register `AuditLogOptions` once), so a store-less audit log cannot be registered by accident.
- `HeadlessAuditLogSetupBuilder` — fluent builder passed to `AddHeadlessAuditLog(setup => ...)`; exposes `ConfigureOptions`, `ConfigureStorage`, and `RegisterExtension`.
- `HeadlessAuditLogBuilder` — returned by `AddHeadlessAuditLog(setup => ...)`; exposes the underlying `IServiceCollection`.
- `IAuditLogStorageOptionsExtension` — setup-time hook implemented by storage provider packages.
- `AuditLogOptionsValidator` — validates transform-sensitive-data configuration at startup.

## Installation

```bash
dotnet add package Headless.AuditLog.Core
```

Add exactly one storage provider package too:

- `dotnet add package Headless.AuditLog.Storage.EntityFramework`
- `dotnet add package Headless.AuditLog.Storage.PostgreSql`
- `dotnet add package Headless.AuditLog.Storage.SqlServer`

## Quick Start

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureOptions(options =>
    {
        options.SensitiveDataStrategy = SensitiveDataStrategy.Redact;
    });

    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.TableName = "audit_log";
    });

    setup.UseEntityFramework<AppDbContext>();
});
```

## Configuration

Configure audit behavior through `setup.ConfigureOptions(...)` and storage shape through `setup.ConfigureStorage(...)`. Then select exactly one storage provider by calling the provider extension, such as `UseEntityFramework<TContext>()`, from the installed storage package.

## Dependencies

- `Headless.AuditLog.Abstractions`
- `Headless.Checks`
- `Headless.Hosting`
- `FluentValidation`
- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Options`

## Side Effects

- Registers `AuditLogOptions` with startup validation.
- Configures `AuditLogStorageOptions`.
- Runs the selected storage provider's setup extension.
