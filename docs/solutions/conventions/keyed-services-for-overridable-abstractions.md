---
title: "Inject framework abstractions (IGuidGenerator, IJsonSerializer); use keyed DI for feature-scoped overrides"
date: 2026-06-07
category: conventions
module: Headless.Coordination.Core
problem_type: convention
component: service_class
severity: medium
related_components:
  - Headless.Core
  - Headless.Serializer.Abstractions
  - Headless.Extensions
applies_when:
  - "A feature needs its own overridable configuration of a framework abstraction (IJsonSerializer, IGuidGenerator) independent of the app-wide default"
  - "Registering a library service that consumers must be able to swap without duplicate descriptors"
  - "Wiring DI in a Setup{Provider} extension or _Add{Feature}Core helper"
  - "Injecting a backend-specific strategy variant of a shared abstraction"
tags:
  - dependency-injection
  - keyed-services
  - tryadd
  - overridable-defaults
  - from-keyed-services
  - serialization
  - guid-generator
  - di-conventions
---

# Inject framework abstractions (IGuidGenerator, IJsonSerializer); use keyed DI for feature-scoped overrides

## Context

The framework exposes shared primitives as injectable abstractions — `IGuidGenerator` over `Guid.NewGuid()`, `IJsonSerializer` over `System.Text.Json.JsonSerializer`. Feature code that reaches for the static primitive bakes in a single behavior (a fixed GUID layout, fixed serializer options) and loses both testability and consumer override. The recurring question when wiring such an abstraction into a new package is *how* to register the default: as a single app-wide service, or as a per-feature instance the consumer can tune in isolation.

Two registration shapes answer this. An **unkeyed** `TryAddSingleton<T>` supplies one global default — fine when a single configuration suffices for the whole app. A **keyed** `TryAddKeyedSingleton<T>(key, factory)` supplies a default scoped to a stable key, letting one feature or backend override the abstraction's configuration without disturbing the app-wide service. The Coordination package (PR #416) added a keyed `IJsonSerializer` so its membership stores serialize metadata/endpoints under their own JSON options, following the precedent already set by `IGuidGenerator` in `Headless.Core`.

## Guidance

**Depend on the abstraction, not the static primitive.** In feature code, inject `IGuidGenerator` / `IJsonSerializer` rather than calling `Guid.NewGuid()` or `JsonSerializer.Serialize`. This lets consumers swap implementations and options, and lets tests substitute deterministic doubles.

**Register library defaults with `TryAdd*`.** Use `TryAddSingleton` / `TryAddKeyedSingleton` so a consumer's prior registration always wins; the framework only *supplies* a default, it never *forces* one. Never require the consumer to register the service themselves.

**Choose unkeyed vs. keyed by override surface:**

- **Unkeyed** (`TryAddSingleton<T>`) when one global configuration of the abstraction suffices. Backend-agnostic code resolves it directly. This is the default — reach for it first.
- **Keyed** (`TryAddKeyedSingleton<T>(KEY, factory)`) when a feature or backend needs its *own* configuration of a shared abstraction, independent of the app-wide instance. Register a sensible default under a stable key, inject it with `[FromKeyedServices(KEY)]`, and let consumers override by pre-registering their own keyed service under the same key — the `TryAdd` default then backs off.

**Expose the key as `public const string` (or a public enum value).** Mark the owning type `[PublicAPI]` and surface the key as a compile-time constant so consumers have a typed link to override against — the same discipline the framework applies to error codes and other service keys. Keys need not be strings: `IGuidGenerator` keys on the `SequentialGuidType` enum.

**Ordering is load-bearing — `TryAdd*` is first-writer-wins.** Whichever descriptor reaches the `(service-type, key)` slot first occupies it. The framework registers its default with `TryAddKeyedSingleton`, so a consumer override must run *before* the framework's `Add{Feature}` call for the framework default to back off cleanly. Note the deliberate asymmetry: the framework uses `TryAdd` (don't clobber the consumer), the consumer uses plain `AddKeyedSingleton` (intentionally claim the slot). Do not "simplify" the consumer registration to `TryAdd` — that would make it order-fragile in the reverse direction.

**Trade-off — keyed is not free.** It adds a key constant, `[FromKeyedServices]` ceremony at every injection site, and a less obvious resolution path (the service no longer falls out of the default container lookup). Reach for keyed only when per-feature override is a genuine requirement, not as a reflex. When in doubt, unkeyed.

## Why This Matters

Injecting the abstraction is what makes the code testable and swappable at all; a `Guid.NewGuid()` call buried in a store has neither property. Keyed registration then solves a narrower problem: it lets two parts of the same app hold *different* configurations of the same contract. Coordination's membership stores must serialize node metadata under controlled options regardless of what JSON the surrounding app uses — without a keyed registration, tuning coordination's serializer would silently retune (or be retuned by) the global `IJsonSerializer`, coupling unrelated subsystems. The keyed key, exposed as a public constant, turns "override coordination's serializer" into a one-line, compile-checked operation instead of a fragile re-registration race. `TryAdd` semantics keep the default non-intrusive: it only fills the slot the consumer left empty.

## When to Apply

**Apply the abstraction-over-primitive rule always** in framework feature code — never inline `Guid.NewGuid()` or `JsonSerializer` where an injected `IGuidGenerator` / `IJsonSerializer` is available.

**Apply the keyed-registration pattern when all of these hold:**

- A feature/backend needs its own configuration of a shared abstraction, distinct from the app-wide default.
- That override must be possible *without* the consumer touching the global service.
- The override is a real, anticipated requirement (e.g., a persisted backend pinning a specific serialization or GUID layout).

**Stay unkeyed when:** one global configuration covers every consumer of the abstraction, or when the abstraction has no per-feature variance. Adding a key "just in case" imposes `[FromKeyedServices]` ceremony with no payoff.

## Examples

**`IGuidGenerator` — enum-keyed precedent** (`src/Headless.Core/SetupGuidGenerator.cs`). Persisted backends resolve a layout by enum key; backend-agnostic code uses the unkeyed default. Note the key is a `SequentialGuidType` enum, not a string:

```csharp
public IServiceCollection AddHeadlessGuidGenerator(SequentialGuidType defaultType = SequentialGuidType.Version7)
{
    services.TryAddKeyedSingleton<IGuidGenerator>(
        SequentialGuidType.Version7,
        static (_, _) => new SequentialGuidGenerator(SequentialGuidType.Version7)
    );
    services.TryAddKeyedSingleton<IGuidGenerator>(
        SequentialGuidType.SqlServer,
        static (_, _) => new SequentialGuidGenerator(SequentialGuidType.SqlServer)
    );
    services.TryAddSingleton<IGuidGenerator>(new SequentialGuidGenerator(defaultType)); // unkeyed default

    return services;
}
```

A SqlServer-backed store injects `[FromKeyedServices(SequentialGuidType.SqlServer)] IGuidGenerator` for SqlServer-sequential GUIDs; everything else takes the unkeyed default.

**`IJsonSerializer` — string-keyed, Coordination** (PR #416). The key is a public constant on the options type (`src/Headless.Coordination.Abstractions/CoordinationOptions.cs`):

```csharp
[PublicAPI]
public sealed class CoordinationOptions
{
    /// <summary>
    /// DI key for the IJsonSerializer used to (de)serialize coordination metadata/endpoints. Consumers can
    /// pre-register their own keyed serializer under this key to override coordination's serialization
    /// independently of the global IJsonSerializer.
    /// </summary>
    public const string JsonSerializerServiceKey = "Headless:Coordination:JsonSerializer";
    // ...
}
```

The default is registered keyed in `_AddCoordinationCore` (`src/Headless.Coordination.Core/Setup.cs`):

```csharp
// Keyed so consumers can override coordination metadata/endpoint serialization independently of the
// global IJsonSerializer by pre-registering their own keyed serializer under the same key.
services.TryAddKeyedSingleton<IJsonSerializer>(
    CoordinationOptions.JsonSerializerServiceKey,
    static (_, _) => new SystemJsonSerializer(new DefaultJsonOptionsProvider())
);
```

Each store injects it via the constant (`src/Headless.Coordination.Redis/RedisMembershipStore.cs`):

```csharp
internal sealed class RedisMembershipStore(
    IConnectionMultiplexer multiplexer,
    /* ... */
    [FromKeyedServices(CoordinationOptions.JsonSerializerServiceKey)] IJsonSerializer serializer
) : IMembershipStore
```

**Consumer override** — register your own keyed serializer *before* `AddHeadlessCoordination(...)`; the framework's `TryAdd` default then backs off:

```csharp
services.AddKeyedSingleton<IJsonSerializer>(
    CoordinationOptions.JsonSerializerServiceKey,
    (sp, _) => new SystemJsonSerializer(myCustomJsonOptionsProvider));
// ...then AddHeadlessCoordination(...) — its TryAddKeyedSingleton sees the existing
// descriptor and no-ops. Order matters: register the override FIRST.
```

The app-wide `IJsonSerializer` is untouched; only coordination's metadata/endpoint serialization changes. (If the override is registered *after* `AddHeadlessCoordination`, the consumer's plain `AddKeyedSingleton` still wins at resolution time, but two descriptors then exist for the key — registering first keeps a single clean descriptor.)

## Related

- [Keyed DI isolation for framework-internal services](../architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md) — the keyed-DI shadowing/isolation rationale and `[FromKeyedServices]` mechanics this convention generalizes from the messaging-lock special case to a framework-wide rule.
- [Unified provider setup builder pattern](../architecture-patterns/unified-provider-setup-builder-pattern.md) — the `Setup{Provider}` / `TryAdd*` root-registration conventions these registrations live within.
- PR #416 — introduced the coordination keyed `IJsonSerializer` example documented here.
