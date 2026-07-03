# Headless.PushNotifications.Core

Setup builder, registration gates, and the named-service provider for the push-notifications abstraction.

## Problem Solved

Owns the unified push-notifications setup builder (`AddHeadlessPushNotifications`) and the `IPushNotificationServiceProvider` implementation, giving every provider one registration grammar (a default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

## Key Features

- `AddHeadlessPushNotifications(Action<HeadlessPushNotificationsSetupBuilder>)` — the single provider-agnostic registration entry point, with an at-most-one-default-provider gate and a once-per-collection guard.
- `HeadlessPushNotificationsSetupBuilder` — receives the optional default `Use*` selection plus `AddNamed(name, …)` named instances; `HeadlessPushNotificationsInstanceBuilder` — the per-named-instance builder that providers extend with their `Use*` members.
- `IPushNotificationServiceProvider` — registered automatically by the gate (keyed-service-backed via `KeyedServicePushNotificationServiceProvider`); resolves named services by name and exposes `RegisteredNames` (the registered named instances, default excluded) for validating a name before resolving.
- Deferred registration: provider contributions are queued and run only after the gates pass — the default first, then each named instance — so a setup that fails a gate leaves the `IServiceCollection` unchanged.

## Design Notes

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate is **per-slot**: it allows at most one default provider (rejecting a second, but permitting zero for a named-only host) while allowing unbounded ordinal-unique named instances, and rejects a repeated `AddHeadlessPushNotifications` on the same `IServiceCollection` (a marker service enforces the single-call rule). Providers contribute deferred `Action<IServiceCollection>` registrations (`RegisterDefaultProvider` for the default, `instance.RegisterProvider` for a named instance) rather than implementing a provider interface, keeping the default and named paths symmetric. `IPushNotificationServiceProvider` resolves only named (keyed) services — the default service, when configured, is the unkeyed `IPushNotificationService`, reachable directly and never by name — and `IPushNotificationServiceProvider.RegisteredNames` enumerates the named instances.

## Installation

```bash
dotnet add package Headless.PushNotifications.Core
```

## Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseNoop();                                // default (optional)
    setup.AddNamed("driver-app", i => i.UseNoop()); // optional named service, keyed "driver-app"
});

// Resolve a named service:
var driver = serviceProvider.GetRequiredService<IPushNotificationServiceProvider>().GetService("driver-app");
```

## Configuration

No configuration required.

## Dependencies

- `Headless.PushNotifications.Abstractions`
- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

`AddHeadlessPushNotifications` registers a provider-registration marker and `IPushNotificationServiceProvider` (keyed-service-backed), then runs the default provider's wiring (the unkeyed `IPushNotificationService`) when a default is configured, followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.
