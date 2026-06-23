---
title: Named-instance registration with keyed services and named options
date: 2026-06-20
category: architecture-patterns
module: Headless.Blobs
problem_type: architecture_pattern
component: service_class
severity: medium
applies_when:
  - Adding multi-instance or named registration to an abstraction-plus-provider feature
  - A provider currently registers its client, normalizer, or options as shared DI singletons
  - The same provider must be registerable more than once with isolated configuration
tags:
  - blobs
  - keyed-services
  - named-options
  - dependency-injection
  - setup-builder
  - per-instance-isolation
---

# Named-instance registration with keyed services and named options

## Context

The framework's abstraction-plus-provider features historically registered a single service instance (`AddSingleton<IService, Engine>()`) plus shared singletons for the provider's client, options, and normalizer. That shape allows exactly one instance per container — a second `Add…` call silently shadows the first. When a feature must support **multiple named instances in one container** (e.g. blob stores `images`→R2, `docs`→Azure, plus two R2 buckets), every shared singleton becomes a contention point. `Headless.Blobs.Core` (the `AddHeadlessBlobs` builder) solved this by mirroring `Headless.Caching.Core`. This documents the reusable wiring so the next feature that needs named instances does not rediscover the gotchas.

## Guidance

Compose three pieces per named instance — the **named-options + keyed-factory + per-instance-dependency** triad:

1. **Bind named options**, not the default singleton:
   `services.Configure<TOptions, TValidator>(setupAction, name)` (the `Headless.Hosting` validated overload). The trailing `name` argument is what makes it a *named* options instance.
2. **Register the engine as a keyed singleton** built by a factory that reads the named options:
   `services.AddKeyedSingleton<IService>(name, (sp, _) => new Engine(...))`.
3. **Construct per-instance dependencies inside the factory** — never resolve them from a shared DI singleton:
   - Options: wrap the named snapshot for engines that take `IOptions<T>` → `Options.Create(sp.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name))`.
   - Provider client: build it from the named options (add a small `internal static` client factory if none exists).
   - Normalizer / other per-config helpers: `new XxxNormalizer()` per instance.
   - Genuinely shared, config-independent ambient services (e.g. `IMimeTypeProvider`, `IClock`, `TimeProvider`) stay resolved from `sp` — sharing them is safe.

Then expose resolution two ways, matching the caching precedent: a name-resolving provider (`IBlobStorageProvider` over `GetKeyedService<IService>(name)`) **and** keyed injection (`[FromKeyedServices("name")] IService`). The **default** (unnamed) instance stays a plain unkeyed `AddSingleton<IService>` so direct injection keeps working; named instances never touch the unkeyed registration.

Provider packages contribute through C# 14 extension members: default overloads on the builder, named overloads on the instance builder, the standard options overload trio (`Action<TOptions>`, `IConfiguration`, `Action<TOptions, IServiceProvider>`). See the broader contract in [unified-provider-setup-builder-pattern.md](unified-provider-setup-builder-pattern.md) and the keyed-DI conventions in [keyed-services-for-overridable-abstractions.md](keyed-services-for-overridable-abstractions.md).

## Why This Matters

Shared singletons silently break multi-instance isolation in ways that compile cleanly and only surface at runtime:

- A DI-singleton client (e.g. AWS `TryAddAWSService<IAmazonS3>`) is *one* client per container — two named AWS stores with different credentials would share it.
- A global option mutation leaks across instances: Cloudflare R2 previously called `services.Configure<AwsBlobStorageOptions>(…)` (unnamed) to force R2-safe settings on the reused S3 engine. With a coexisting AWS store, that global mutation would silently rewrite the AWS store's behavior. Binding the forced settings to a *named* `AwsBlobStorageOptions` (`Configure<AwsBlobStorageOptions>(name, …)`) keeps each instance independent.
- A shared `IBlobNamingNormalizer` (`TryAddSingleton`) would apply one provider's bucket rules to another provider's store.

Constructing these inside the keyed factory makes each instance fully self-contained, which is exactly what a name-addressed API promises.

## When to Apply

- A feature with the abstraction-plus-provider shape needs more than one configured instance per container.
- The same provider must be registerable N times (multi-account, multi-bucket, prod+staging).
- Avoid the keyed ceremony when only one instance is ever needed — a plain `AddSingleton` default is simpler.

## Examples

Default (unkeyed) + named (keyed) for one provider, with per-instance client and normalizer:

```csharp
// Default store — plain unkeyed IBlobStorage
setup.RegisterDefaultProvider(services =>
{
    services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(setupAction);
    services.AddBlobStorageProvider();
    services.AddSingleton<IBlobStorage>(sp => new AwsBlobStorage(
        S3ClientFactory.Create(awsOptions),              // per-instance client
        sp.GetRequiredService<IMimeTypeProvider>(),       // shared ambient — safe
        sp.GetRequiredService<IClock>(),
        sp.GetRequiredService<IOptions<AwsBlobStorageOptions>>(),
        new AwsBlobNamingNormalizer(),                    // per-instance normalizer
        sp.GetService<ILogger<AwsBlobStorage>>()));
    services.AddSingleton<IPresignedUrlBlobStorage>(sp =>
        (IPresignedUrlBlobStorage)sp.GetRequiredService<IBlobStorage>()); // capability forward
});

// Named store — keyed IBlobStorage, reads named options via IOptionsMonitor.Get(name)
instance.RegisterProvider(services =>
{
    services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(setupAction, name);
    services.AddBlobStorageProvider();
    services.AddKeyedSingleton<IBlobStorage>(name, (sp, _) => new AwsBlobStorage(
        S3ClientFactory.Create(awsOptions),
        sp.GetRequiredService<IMimeTypeProvider>(),
        sp.GetRequiredService<IClock>(),
        Options.Create(sp.GetRequiredService<IOptionsMonitor<AwsBlobStorageOptions>>().Get(name)),
        new AwsBlobNamingNormalizer(),
        sp.GetService<ILogger<AwsBlobStorage>>()));
    services.AddKeyedSingleton<IPresignedUrlBlobStorage>(name, (sp, _) =>
        (IPresignedUrlBlobStorage)sp.GetRequiredKeyedService<IBlobStorage>(name));
});
```

Engine-shape gotchas encountered across the six blob providers:

- **Engine takes `IOptions<T>`** (AWS, Azure, FileSystem, Redis): wrap with `Options.Create(monitor.Get(name))` in the factory — no engine change needed.
- **Engine takes `IOptionsMonitor<T>` and reads `.CurrentValue`** (SshNet): pass a monitor whose `CurrentValue` is the named snapshot (a thin `IOptionsMonitor` wrapper around `monitor.Get(name)`), so the instance only ever sees its own config.
- **Per-instance `IDisposable` resource** (SshNet `SftpClientPool`): register it as a *keyed* singleton (`AddKeyedSingleton<SftpClientPool>(name, …)`) so the container owns disposal; the engine's `DisposeAsync` then does nothing.
- **No DI-registered client** (Redis carries `IConnectionMultiplexer` in its options; FileSystem has none): isolation comes for free from the named options — nothing extra to instance.
- **Multiple `extension(...)` blocks in one setup class** emit marker members differing only by case → add `#pragma warning disable CA1708` above the namespace (warnings are errors in CI).

## Related

- [unified-provider-setup-builder-pattern.md](unified-provider-setup-builder-pattern.md) — the per-slot builder contract (default/named/cross-cutting slots, deferred registration, called-once marker, overload trio).
- [keyed-services-for-overridable-abstractions.md](keyed-services-for-overridable-abstractions.md) — `TryAddKeyedSingleton` defaults vs consumer `AddKeyedSingleton`, key-visibility discipline.
- Reference implementation: `src/Headless.Blobs.Core/` (`HeadlessBlobsSetupBuilder`, `Setup.cs`, `KeyedServiceBlobStorageProvider`) and `src/Headless.Caching.Core/` (the original of this shape).
