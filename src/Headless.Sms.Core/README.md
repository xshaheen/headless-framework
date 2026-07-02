# Headless.Sms.Core

Setup builder, registration gates, and the named-sender provider for the SMS abstraction.

## Problem Solved

Owns the unified SMS setup builder (`AddHeadlessSms`) and the `ISmsSenderProvider` implementation, giving every provider one registration grammar (a default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

## Key Features

- `AddHeadlessSms(Action<HeadlessSmsSetupBuilder>)` — the single provider-agnostic registration entry point, with an at-most-one-default-provider gate and a once-per-collection guard.
- `HeadlessSmsSetupBuilder` — receives the optional default `Use*` selection plus `AddNamed(name, …)` named instances; `HeadlessSmsInstanceBuilder` — the per-named-instance builder that providers extend with their `Use*` members.
- `ISmsSenderProvider` — registered automatically by the gate (keyed-service-backed via `KeyedServiceSmsSenderProvider`); resolves named senders by name and exposes `RegisteredNames` (the registered named instances, default excluded) for validating a name before resolving.
- Deferred registration: provider contributions are queued and run only after the gates pass — the default first, then each named instance — so a setup that fails a gate leaves the `IServiceCollection` unchanged.

## Design Notes

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate is **per-slot**: it allows at most one default provider (rejecting a second, but permitting zero for a named-only host) while allowing unbounded ordinal-unique named instances, and rejects a repeated `AddHeadlessSms` on the same `IServiceCollection` (a marker service enforces the single-call rule). Providers contribute deferred `Action<IServiceCollection>` registrations (`RegisterDefaultProvider` for the default, `instance.RegisterProvider` for a named instance) rather than implementing a provider interface, keeping the default and named paths symmetric. `ISmsSenderProvider` resolves only named (keyed) senders — the default sender, when configured, is the unkeyed `ISmsSender`, reachable directly and never by name — and `ISmsSenderProvider.RegisteredNames` enumerates the named instances.

## Installation

```bash
dotnet add package Headless.Sms.Core
```

## Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseNoop();                             // default (optional)
    setup.AddNamed("otp", i => i.UseNoop());     // optional named sender, keyed "otp"
});

// Resolve a named sender:
var otp = serviceProvider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
```

## Configuration

No configuration required.

## Dependencies

- `Headless.Sms.Abstractions`
- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

## Side Effects

`AddHeadlessSms` registers a provider-registration marker and `ISmsSenderProvider` (keyed-service-backed), then runs the default provider's wiring (the unkeyed `ISmsSender`) when a default is configured, followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.
