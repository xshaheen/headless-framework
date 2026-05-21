---
title: "Isolate framework-internal services via keyed DI to prevent app-level shadowing"
date: 2026-05-19
module: Headless.Messaging.Core
component: service_class
problem_type: architecture_pattern
severity: medium
tags: ["keyed-di", "distributed-lock", "messaging", "service-isolation", "dotnet"]
---

## Context

`Setup.cs` registered the NoOp fallback as an unkeyed singleton:

```csharp
services.TryAddSingleton<IDistributedLockProvider, NoOpDistributedLockProvider>();
```

`TryAdd*` loses when a prior registration already exists — but it also loses when the *consumer app* registers its own `IDistributedLockProvider` for a completely unrelated purpose (e.g., a Redis lock provider for its own business logic). That app-level provider silently shadows messaging's fallback. The Bootstrapper's `_WarnIfNoOpProvider` resolved the unkeyed service, so it saw the app's real provider, suppressed EventId 77, and messaging's retry processor ran with no mutual exclusion and no warning.

The same pattern recurs whenever a framework module needs a service the consumer might independently register for unrelated purposes.

## Guidance

Register framework-internal service instances under a named key rather than as unkeyed singletons.

**NoOp fallback** — use `TryAddKeyedSingleton` so it yields to `UseDistributedLock`:

```csharp
// Setup.cs — falls back to NoOp; yields to any AddKeyedSingleton call with the same key
services.TryAddKeyedSingleton<IDistributedLockProvider, NoOpDistributedLockProvider>(MessagingKeys.LockProvider);
```

**Real provider** — use `AddKeyedSingleton` (non-Try) so last-wins resolution replaces the NoOp:

```csharp
// MessagingBuilder.UseDistributedLock — replaces the NoOp via last-wins keyed resolution
Services.AddKeyedSingleton<IDistributedLockProvider>(MessagingKeys.LockProvider, provider);
Services.Configure<MessagingOptions>(o => o.UseStorageLock = true);
```

**Consumer constructor** — inject via `[FromKeyedServices]`:

```csharp
// MessageNeedToRetryProcessor
public MessageNeedToRetryProcessor(
    [FromKeyedServices(MessagingKeys.LockProvider)] IDistributedLockProvider lockProvider,
    ...
)
```

**Hide the key** — expose only a public builder API; never expose `MessagingKeys` outside the assembly:

```csharp
// MessagingBuilder.cs
public MessagingBuilder UseDistributedLock(IDistributedLockProvider provider) { ... }
public MessagingBuilder UseDistributedLock(Func<IServiceProvider, IDistributedLockProvider> factory) { ... }
```

Callers configure the provider through the builder API. They cannot accidentally collide with the internal key and cannot suppress the EventId 77 diagnostic by registering an unkeyed `IDistributedLockProvider` elsewhere.

## Why It Matters

Unkeyed fallbacks are invisible and fragile:

- App registers `IDistributedLockProvider` → messaging silently picks it up → retry mutual exclusion runs with a provider built for something else.
- Worse: app registers a real provider via the unkeyed path → `_WarnIfNoOpProvider` resolves that real provider → EventId 77 is suppressed even though messaging was never explicitly configured.

Keyed isolation makes the coupling explicit. The framework owns its slice of the DI container, and the app owns its own. Misconfiguration now requires affirmative action (calling `UseDistributedLock`), and the warning fires whenever that affirmative step is missing.

## When to Apply

Use keyed DI isolation whenever:

1. A framework module registers a service interface that is broadly recognized (e.g., `IDistributedLockProvider`, `IMemoryCache`, `IHttpClientFactory` specializations).
2. There is a meaningful NoOp or degraded fallback that should not silently accept an unrelated app-level registration.
3. The framework controls when the "real" provider is swapped in — the swap should be explicit (builder API), not implicit (registration order).

Not needed for interfaces that are framework-specific and have no plausible app-level collision.

## Examples

**Before — unkeyed, shadowing-prone:**

```csharp
// Setup.cs
services.TryAddSingleton<IDistributedLockProvider, NoOpDistributedLockProvider>();

// App registers its own provider for unrelated use:
services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
// ↑ This silently replaces messaging's fallback.
// _WarnIfNoOpProvider resolves the Redis provider → no warning → no real messaging lock.
```

**After — keyed, isolated:**

```csharp
// Setup.cs — NoOp under messaging's private key
services.TryAddKeyedSingleton<IDistributedLockProvider, NoOpDistributedLockProvider>(
    MessagingKeys.LockProvider
);

// App registers its own unkeyed provider — has no effect on messaging.
services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();

// Messaging's bootstrapper resolves the keyed service → NoOp detected → EventId 77 fires.
var lockProvider = serviceProvider.GetRequiredKeyedService<IDistributedLockProvider>(
    MessagingKeys.LockProvider
);
```

**Wiring a real provider (via builder API):**

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UseInMemoryStorage();
    setup.UseConventions(c => { ... });
}).UseDistributedLock(myRedisLockProvider); // replaces NoOp; sets UseStorageLock = true
```
