---
domain: Push Notifications
packages: PushNotifications.Abstractions, PushNotifications.Core, PushNotifications.Dev, PushNotifications.Firebase
---

# Push Notifications

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Default and named clients](#default-and-named-clients)
    - [Response Status Model](#response-status-model)
    - [Provider Selection Model](#provider-selection-model)
    - [Multicast and Batching](#multicast-and-batching)
- [Choosing a Provider](#choosing-a-provider)
- [Headless.PushNotifications.Abstractions](#headlesspushnotificationsabstractions)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.PushNotifications.Core](#headlesspushnotificationscore)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.PushNotifications.Dev](#headlesspushnotificationsdev)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)
- [Headless.PushNotifications.Firebase](#headlesspushnotificationsfirebase)
    - [Problem Solved](#problem-solved-3)
    - [Key Features](#key-features-3)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-3)
    - [Quick Start](#quick-start-3)
    - [Configuration](#configuration-3)
    - [Dependencies](#dependencies-3)
    - [Side Effects](#side-effects-3)

> Provider-agnostic push notification API with Firebase Cloud Messaging for production and a no-op implementation for development, supporting an optional default service plus any number of named (keyed) instances.

## Quick Orientation

Install `Headless.PushNotifications.Abstractions` plus one provider package. Register with `AddHeadlessPushNotifications(setup => setup.Use…())` — at most one **default** `Use*` provider per call (the default is optional; a named-only host is supported), plus any number of **named** services via `setup.AddNamed(name, i => i.Use…())`. Code against `IPushNotificationService` for the default; resolve named services with `IPushNotificationServiceProvider.GetService("name")` or `[FromKeyedServices("name")] IPushNotificationService`. Never reference provider-specific types in application code — swap providers by changing DI registration only.

```csharp
// Production — Firebase Cloud Messaging (default)
builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(builder.Configuration.GetSection("Firebase")));

// Development / testing — no-op, always succeeds
builder.Services.AddHeadlessPushNotifications(setup => setup.UseNoop());

// Default plus named instances (e.g. separate Firebase projects per app):
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase:Primary"));            // default (optional)
    setup.AddNamed("driver-app", i => i.UseFirebase(builder.Configuration.GetSection("Firebase:Driver")));
    setup.AddNamed("rider-app", i => i.UseFirebase(builder.Configuration.GetSection("Firebase:Rider")));
});
```

`Headless.PushNotifications.Core` owns registration (`AddHeadlessPushNotifications`, `HeadlessPushNotificationsSetupBuilder`, `HeadlessPushNotificationsInstanceBuilder`) and the `IPushNotificationServiceProvider` implementation over keyed DI. Providers pull it transitively — you rarely install it directly. `Headless.PushNotifications.Abstractions` holds contracts only (`IPushNotificationService`, `IPushNotificationServiceProvider`, response types).

Use `IPushNotificationService.SendToDeviceAsync` for single-device delivery and `SendMulticastAsync` for batch sends. Always check `PushNotificationResponse.Status` — three states apply: `Success`, `Failure`, and `Unregistered`.

## Agent Instructions

- Register at most one **default** provider per container: `services.AddHeadlessPushNotifications(setup => setup.Use…())`. The default is optional — zero defaults is allowed (a named-only host). Multiple default providers in one delegate, or a repeated `AddHeadlessPushNotifications` on the same `IServiceCollection`, throws `InvalidOperationException` at registration time. The available default `Use*` calls are `UseFirebase` and `UseNoop` — the same set is available on each named instance.
- Add **named** services in the same call: `setup.AddNamed("name", i => i.Use…())`. Names must be non-whitespace and ordinal-unique within the call, and each named instance must select exactly one provider — a duplicate name, whitespace name, or zero/multiple providers throws at registration time. The default is optional; a named-only host (no default) is supported — the unkeyed `IPushNotificationService` is simply not registered when no default is configured.
- Resolve a named service with `IPushNotificationServiceProvider.GetService("name")` (throws `InvalidOperationException` naming `AddNamed` when unregistered) / `GetServiceOrNull("name")` (returns `null`), or raw keyed DI (`[FromKeyedServices("name")] IPushNotificationService`, `GetRequiredKeyedService<IPushNotificationService>(name)`). Both `GetService` and `GetServiceOrNull` throw `ArgumentException` on a null/whitespace name. The default (unkeyed) `IPushNotificationService` is **not** exposed through `IPushNotificationServiceProvider`. To validate an externally supplied name before resolving, check `IPushNotificationServiceProvider.RegisteredNames` (the registered named-instance names, an `IReadOnlySet<string>`; the default is excluded) instead of probing `GetServiceOrNull` and handling `null`.
- Registration is deferred: provider contributions are queued and nothing touches the `IServiceCollection` until the gates pass, so a setup that throws leaves the collection unchanged. The same provider can back two different names with fully independent options.
- Each named Firebase instance isolates its own options (validated per name via FluentValidation + `ValidateOnStart`), its own retry pipeline (keyed `Headless:FcmRetry:{name}`), and its own lazily-created `FirebaseApp`. Keyed DI does not cascade the key to constructor dependencies, so named services never read the default configuration (a keyed sender reads `IOptionsMonitor.Get(name)`, never `CurrentValue`).
- Always code against `IPushNotificationService` from `Headless.PushNotifications.Abstractions`. Never reference `FcmPushNotificationService` or other concrete types in application code.
- Use `Headless.PushNotifications.Dev` (`UseNoop()`) in development and testing environments to avoid sending real notifications. Switch on `builder.Environment.IsDevelopment()`.
- Do NOT call the Firebase Admin SDK (`FirebaseAdmin`, `FirebaseMessaging`) directly. Route all sends through `IPushNotificationService`.
- After every send, check `PushNotificationResponse.Status`. Three distinct states exist — `Success`, `Failure`, and `Unregistered` — and `IsSucceeded()` / `IsFailed()` both return `false` for an unregistered token. Use `IsUnregistered()` explicitly and remove that token from your store.
- FCM enforces content limits: **title ≤ 100 characters**, **body ≤ 4 000 characters**. `FcmPushNotificationService` throws `ArgumentException` if either limit is exceeded.
- FCM data payload keys `from`, `notification`, `message_type`, and any key starting with `google` or `gcm` are reserved. Passing a reserved key throws `ArgumentException` from `SendToDeviceAsync` / `SendMulticastAsync` before any network call.
- Multicast sends are transparently chunked into batches of at most 500 tokens (the FCM hard limit). A single `SendMulticastAsync` call handles any number of tokens.
- Firebase retries transient errors (HTTP 429 `QuotaExceeded`, 503 `Unavailable`, 500 `Internal`, network errors, non-user timeouts) automatically with exponential backoff. Do not wrap calls in your own retry for these errors.
- Permanent errors (`Unregistered`, `InvalidArgument`, `SenderIdMismatch`, `ThirdPartyAuthError`) are not retried. `Unregistered` is returned as `PushNotificationResponseStatus.Unregistered`, not as a failure.
- Do not disable retry in production (`MaxAttempts = 0`) unless you have your own resilience infrastructure. Default is 5 attempts with exponential backoff and jitter.
- `FirebaseOptions.Json` contains sensitive private-key material. Do not log it, serialize it, or store it in configuration as plain text in production. The `ToString()` override on `FirebaseOptions` redacts it.

## Core Concepts

### Default and named clients

A host registers an optional **default** service plus any number of **named** services in a single `AddHeadlessPushNotifications` call. This mirrors the SMS and Emails features' named-instance pattern (`ISmsSenderProvider` / `IEmailSenderProvider` + `AddNamed` + keyed registrations).

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Push:Primary"));                 // default (optional)
    setup.AddNamed("driver-app", i => i.UseFirebase(builder.Configuration.GetSection("Push:Driver"))); // named, keyed "driver-app"
    setup.AddNamed("test-sink", i => i.UseNoop());                                        // named, keyed "test-sink"
});
```

- **Default is optional, named is additive.** The gate rejects more than one default provider but allows zero; named instances are unbounded and exempt from the default gate. The unkeyed `IPushNotificationService` resolves only when a default is configured — a named-only host is supported.
- **Names are validated at registration time.** Each name must be non-whitespace and ordinal-unique within the call; each named instance must select exactly one provider. Violations throw `ArgumentException` / `InvalidOperationException`.
- **Resolution.** Named services resolve two ways — as keyed services (`[FromKeyedServices("driver-app")] IPushNotificationService`) and through `IPushNotificationServiceProvider.GetService(name)` (throws when unregistered) / `GetServiceOrNull(name)` (returns `null`). `IPushNotificationServiceProvider.RegisteredNames` enumerates the registered named instances (default excluded) for validating a name before resolving. The default service resolves as the unkeyed `IPushNotificationService` and is **not** exposed through `IPushNotificationServiceProvider`.
- **Isolation (Firebase).** Each named instance keys its options and backend under its name: per-name options (`IOptionsMonitor<FirebaseOptions>.Get(name)`, validated on start), a per-name retry pipeline keyed `Headless:FcmRetry:{name}`, and its own lazily-created `FirebaseApp` (so distinct service-account credentials never collide). .NET keyed registrations do not cascade the key to a type's constructor dependencies, so every keyed service/sender is an explicit factory — named push notifications never flow through the default configuration.

### Response Status Model

Every send returns a `PushNotificationResponse` with one of three mutually exclusive states:

| `Status` | `IsSucceeded()` | `IsFailed()` | `IsUnregistered()` | Meaning |
|---|---|---|---|---|
| `Success` | `true` | `false` | `false` | Accepted by FCM; `MessageId` is non-null |
| `Failure` | `false` | `true` | `false` | Provider rejected the message; `FailureError` is non-null |
| `Unregistered` | `false` | `false` | `true` | Token is no longer valid; remove from your store |

The `Unregistered` state is not a failure in the FCM model — it is a signal to clean up stale tokens. A simple `if (!response.IsSucceeded())` will miss the unregistered case.

### Provider Selection Model

`AddHeadlessPushNotifications` accepts a callback that receives a `HeadlessPushNotificationsSetupBuilder`. Provider packages each contribute a `Use{Provider}` C# 14 extension member on that builder (default slot) and on `HeadlessPushNotificationsInstanceBuilder` (named slot). The framework enforces **at most one default** provider per registration, plus unbounded named instances — this prevents accidental dual-registration of the default while making per-instance routing explicit. Providers contribute deferred `Action<IServiceCollection>` registrations rather than implementing a provider-options interface, keeping the default and named paths symmetric.

### Multicast and Batching

`SendMulticastAsync` sends the same notification to many device tokens. The FCM implementation automatically chunks token lists into batches of ≤ 500 (the FCM limit) and aggregates results into a single `BatchPushNotificationResponse`. The `Responses` list has exactly one entry per input token, preserving order. A whole-batch transport failure after all retries is surfaced as a `Failure` outcome for every token in that batch rather than thrown, so earlier-batch results are never discarded.

## Choosing a Provider

| | Firebase (`Headless.PushNotifications.Firebase`) | Dev no-op (`Headless.PushNotifications.Dev`) |
|---|---|---|
| **Use when** | Production mobile apps (Android, iOS via APNs bridge, Web) | Local development, test suites, CI |
| **Avoid when** | Local development (real credentials, real sends) | Any production environment |
| **Backend** | Firebase Cloud Messaging (FCM v1 API via `FirebaseAdmin`) | In-process stub |
| **Credentials** | Firebase service account JSON (`FirebaseOptions.Json`) | None |
| **Retry** | Automatic exponential backoff for transient FCM errors | N/A |
| **Trade-off** | Requires a Firebase project and service account | Zero external dependencies; always succeeds |

---
## Headless.PushNotifications.Abstractions

Defines the unified interface and contract types for push notification services.

### Problem Solved

Provides a provider-agnostic push notification API so application code never depends on a specific backend. Switching from the dev no-op to Firebase for production requires only a DI registration change.

### Key Features

- `IPushNotificationService` — core sending interface:
  - `SendToDeviceAsync(clientToken, title, body, data?, ct)` — single-device delivery
  - `SendMulticastAsync(clientTokens, title, body, data?, ct)` — batch delivery
- `IPushNotificationServiceProvider` — resolves named services by name: `GetService(name)` (throws when unregistered) and `GetServiceOrNull(name)` (returns `null`), plus `RegisteredNames` (`IReadOnlySet<string>`) listing the registered named instances (the default is excluded) so an externally supplied name can be validated before resolving. Backed by the container's keyed `IPushNotificationService` registrations; the concrete implementation lives in `Headless.PushNotifications.Core`.
- `PushNotificationResponse` — single-device outcome with three states (`Success`, `Failure`, `Unregistered`); factory methods `Succeeded`, `Failed`, `Unregistered`; query methods `IsSucceeded()`, `IsFailed()`, `IsUnregistered()`; properties `Token`, `MessageId?`, `FailureError?`, `Status`
- `PushNotificationResponseStatus` enum — `Success`, `Failure`, `Unregistered`
- `BatchPushNotificationResponse` — multicast aggregate: `SuccessCount`, `FailureCount`, `Responses` (one per token)

### Installation

```bash
dotnet add package Headless.PushNotifications.Abstractions
```

### Quick Start

```csharp
public sealed class NotificationService(IPushNotificationService pushService, ILogger<NotificationService> logger)
{
    public async Task SendAsync(string deviceToken, string title, string message, CancellationToken ct)
    {
        var response = await pushService.SendToDeviceAsync(
            deviceToken,
            title,
            message,
            data: new Dictionary<string, string> { ["orderId"] = "123" },
            cancellationToken: ct
        );

        if (response.IsUnregistered())
        {
            // Token is stale — remove it from your store.
            await RemoveTokenAsync(deviceToken, ct);
        }
        else if (response.IsFailed())
        {
            logger.LogError("Push notification failed for token {Token}: {Error}", deviceToken, response.FailureError);
        }
    }

    public async Task SendToManyAsync(IReadOnlyList<string> tokens, string title, string message, CancellationToken ct)
    {
        var result = await pushService.SendMulticastAsync(tokens, title, message, cancellationToken: ct);

        logger.LogInformation("Push sent: {Success}/{Total}", result.SuccessCount, tokens.Count);

        // Remove stale tokens.
        foreach (var r in result.Responses)
        {
            if (r.IsUnregistered())
                await RemoveTokenAsync(r.Token, ct);
        }
    }
}
```

To route to a named instance, take a dependency on `IPushNotificationServiceProvider` and resolve by name (`provider.GetService("driver-app")`), or inject the keyed service directly with `[FromKeyedServices("driver-app")] IPushNotificationService`.

### Configuration

None. This is an abstractions-only package.

### Dependencies

- `Headless.Checks`

### Side Effects

None. This package defines only interfaces and contracts.
---
## Headless.PushNotifications.Core

Setup builder, registration gates, and the named-service provider for the push-notifications abstraction.

### Problem Solved

Owns the unified push-notifications setup builder (`AddHeadlessPushNotifications`) and the `IPushNotificationServiceProvider` implementation, giving every provider one registration grammar (a default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

### Key Features

- `AddHeadlessPushNotifications(Action<HeadlessPushNotificationsSetupBuilder>)` — the single provider-agnostic registration entry point, with an at-most-one-default-provider gate and a once-per-collection guard.
- `HeadlessPushNotificationsSetupBuilder` — receives the optional default `Use*` selection plus `AddNamed(name, …)` named instances; `HeadlessPushNotificationsInstanceBuilder` — the per-named-instance builder that providers extend with their `Use*` members.
- `IPushNotificationServiceProvider` — registered automatically by the gate (keyed-service-backed via `KeyedServicePushNotificationServiceProvider`); resolves named services by name and exposes `RegisteredNames` (the registered named instances, default excluded) for validating a name before resolving.
- Deferred registration: provider contributions are queued and run only after the gates pass — the default first, then each named instance — so a setup that fails a gate leaves the `IServiceCollection` unchanged.

### Design Notes

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate is **per-slot**: it allows at most one default provider (rejecting a second, but permitting zero for a named-only host) while allowing unbounded ordinal-unique named instances, and rejects a repeated `AddHeadlessPushNotifications` on the same `IServiceCollection` (a marker service enforces the single-call rule). Providers contribute deferred `Action<IServiceCollection>` registrations (`RegisterDefaultProvider` for the default, `instance.RegisterProvider` for a named instance) rather than implementing a provider interface, keeping the default and named paths symmetric. `IPushNotificationServiceProvider` resolves only named (keyed) services — the default service, when configured, is the unkeyed `IPushNotificationService`, reachable directly and never by name — and `IPushNotificationServiceProvider.RegisteredNames` enumerates the named instances.

### Installation

```bash
dotnet add package Headless.PushNotifications.Core
```

### Quick Start

```csharp
// Provider-agnostic registration entry point (a provider package supplies the Use* member):
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseNoop();                             // default (optional)
    setup.AddNamed("driver-app", i => i.UseNoop()); // optional named service, keyed "driver-app"
});

// Resolve a named service:
var driver = serviceProvider.GetRequiredService<IPushNotificationServiceProvider>().GetService("driver-app");
```

### Configuration

No configuration required.

### Dependencies

- `Headless.PushNotifications.Abstractions`
- `Headless.Checks`
- `Microsoft.Extensions.DependencyInjection.Abstractions`

### Side Effects

`AddHeadlessPushNotifications` registers a provider-registration marker and `IPushNotificationServiceProvider` (keyed-service-backed), then runs the default provider's wiring (the unkeyed `IPushNotificationService`) when a default is configured, followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.
---
## Headless.PushNotifications.Dev

No-op push notification provider for local development and testing.

### Problem Solved

Prevents real notifications from being sent during development or test runs. Uses the same `IPushNotificationService` interface as production so no application code changes are needed when switching environments.

### Key Features

- Silent `IPushNotificationService` implementation (`NoopPushNotificationService`)
- No network calls or external dependencies
- Always returns `Success` responses with a generated GUID as the message id
- Never validates input or throws (inert for any caller, including invalid tokens or empty titles)
- Selectable as the default (`setup.UseNoop()`) or as a named instance (`setup.AddNamed("name", i => i.UseNoop())`)

### Installation

```bash
dotnet add package Headless.PushNotifications.Dev
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessPushNotifications(setup => setup.UseNoop());
}
else
{
    builder.Services.AddHeadlessPushNotifications(setup =>
        setup.UseFirebase(builder.Configuration.GetSection("Firebase"))
    );
}
```

### Configuration

None. No options or configuration keys.

### Dependencies

- `Headless.PushNotifications.Core`

### Side Effects

- Registers `IPushNotificationService` as singleton (`NoopPushNotificationService`) for the default, or a keyed singleton under the instance name for a named instance
---
## Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation of `IPushNotificationService` for production push notifications.

### Problem Solved

Delivers push notifications to Android (FCM), iOS (via FCM-to-APNs bridge), and Web clients using the FCM v1 API. Handles multicast batching, transient-error retry with exponential backoff, and per-token outcome mapping behind the `IPushNotificationService` interface.

### Key Features

- FCM-backed `IPushNotificationService` implementation (`FcmPushNotificationService`)
- Selectable as the default (`setup.UseFirebase(…)`) or as a named instance (`setup.AddNamed("name", i => i.UseFirebase(…))`), each isolating its own options, retry pipeline, and `FirebaseApp`
- Single-device (`SendToDeviceAsync`) and multicast (`SendMulticastAsync`) delivery
- Automatic chunking of multicast sends into batches of ≤ 500 tokens (FCM hard limit)
- Custom data payload support (with reserved-key enforcement)
- Input validation: title ≤ 100 characters, body ≤ 4 000 characters
- **Automatic retry** for transient failures: exponential backoff with jitter, Retry-After header support for rate limits
- Configurable retry policy (`FirebaseRetryOptions`): `MaxAttempts` (0–10), `MaxDelay`, `RateLimitDelay`, `UseJitter`
- Structured logging and OpenTelemetry Activity events on retry
- Options validated at startup via FluentValidation

### Design Notes

The Firebase Admin SDK `FirebaseApp` is created **lazily on the first send**, not at DI registration time. This means:
- Registration has no observable side effects (no credentials are loaded, no HTTP calls are made).
- Multiple hosts (default plus named instances) in the same process coexist with different credentials — each registration generates a uniquely-named `FirebaseApp`.
- Configuration errors in `FirebaseOptions.Json` (malformed JSON, wrong credential type) surface as exceptions on the first call, not at startup. Supply the `IConfiguration` overload so the options validator catches missing `Json` at startup instead.

Each named instance reads its own options snapshot (`IOptionsMonitor<FirebaseOptions>.Get(name)`) and its own retry pipeline (keyed `Headless:FcmRetry:{name}`); the default reads the unnamed options and the `Headless:FcmRetry` pipeline. Keyed DI does not cascade the key to constructor dependencies, so a keyed sender never reads `CurrentValue` (which binds the default) — the sender is registered through an explicit factory that passes its own name.

Android messages are sent with `Priority.High`; iOS messages include an APNs badge count of 1. These are hardcoded defaults — the `data` payload provides the only customization surface exposed by this abstraction.

### Installation

```bash
dotnet add package Headless.PushNotifications.Firebase
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Recommended: bind from configuration so the options validator runs at startup.
builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(builder.Configuration.GetSection("Firebase")));

// Multiple Firebase projects, one per app:
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase:Primary"));             // default
    setup.AddNamed("driver-app", i => i.UseFirebase(builder.Configuration.GetSection("Firebase:Driver")));
});
```

Sending:

```csharp
var response = await pushService.SendToDeviceAsync(
    clientToken: deviceToken,
    title: "Order shipped",
    body: "Your order #1234 is on its way.",
    data: new Dictionary<string, string> { ["orderId"] = "1234" },
    cancellationToken: ct
);

if (response.IsUnregistered())
    await tokenStore.RemoveAsync(deviceToken, ct);
```

### Configuration

#### appsettings.json

```json
{
  "Firebase": {
    "Json": "{ ...service account JSON... }",
    "Retry": {
      "MaxAttempts": 5,
      "MaxDelay": "00:01:00",
      "RateLimitDelay": "00:01:00",
      "UseJitter": true
    }
  }
}
```

#### FirebaseOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Json` | `string` | _(required)_ | Firebase service account JSON string. Do not log — `ToString()` redacts it. |
| `Retry` | `FirebaseRetryOptions` | see below | Retry policy. |

#### FirebaseRetryOptions

| Property | Type | Default | Range | Description |
|---|---|---|---|---|
| `MaxAttempts` | `int` | `5` | 0–10 | Max retry attempts. `0` disables retry. |
| `MaxDelay` | `TimeSpan` | `00:01:00` | 1s–5min | Cap on any single retry delay. |
| `RateLimitDelay` | `TimeSpan` | `00:01:00` | 1s–5min | Delay for HTTP 429 when no Retry-After header is present. |
| `UseJitter` | `bool` | `true` | — | Adds ±25% variance to prevent thundering herd. |

##### Retry overrides

```csharp
// Supply options with a delegate:
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(options =>
    {
        options.Json = configuration["Firebase:Json"]!;
        options.Retry = new FirebaseRetryOptions { MaxAttempts = 3 };
    })
);

// Or with a pre-built instance:
builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(new FirebaseOptions { Json = json }));

// Disable retry:
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(options =>
    {
        options.Json = json;
        options.Retry = new FirebaseRetryOptions { MaxAttempts = 0 };
    })
);
```

#### Transient Errors (Retried)

| Error | HTTP | Retry delay |
|---|---|---|
| `QuotaExceeded` | 429 | Retry-After header, or `RateLimitDelay` (default 60s), capped at `MaxDelay` |
| `Unavailable` | 503 | Exponential backoff |
| `Internal` | 500 | Exponential backoff |
| `HttpRequestException` | — | Exponential backoff |
| `TaskCanceledException` (timeout only) | — | Exponential backoff |

#### Permanent Errors (No Retry)

| Error | Meaning | Caller action |
|---|---|---|
| `Unregistered` | Token invalid | Returns `PushNotificationResponseStatus.Unregistered`; remove token |
| `InvalidArgument` | Malformed request | Code bug; fix the payload |
| `SenderIdMismatch` | Wrong credentials | Configuration error |
| `ThirdPartyAuthError` | Bad APNs certificate | Configuration error |
| User `CancellationToken` | Caller cancelled | Do not retry |

#### Backoff Strategy

- Initial delay: 1s
- Exponential sequence: 1s → 2s → 4s → 8s → 16s → 32s, capped at `MaxDelay` (default 60s)
- Jitter: ±25% (when `UseJitter = true`)
- Retry pipeline key: `"Headless:FcmRetry"` for the default, `"Headless:FcmRetry:{name}"` per named instance (registered via Polly's `AddResiliencePipeline`)

### Dependencies

- `Headless.PushNotifications.Core`
- `Headless.Hosting`
- `FirebaseAdmin`
- `Microsoft.Extensions.Http.Resilience`
- `Polly.Core`

### Side Effects

- Registers `IPushNotificationService` as singleton (`FcmPushNotificationService`) for the default, or a keyed singleton under the instance name for a named instance
- Registers a `ResiliencePipeline` named `"Headless:FcmRetry"` (default) or `"Headless:FcmRetry:{name}"` (per named instance) via Polly
- Registers `TimeProvider.System` as singleton (if not already registered)
- The Firebase Admin SDK `FirebaseApp` is created lazily on first send; registration has no network side effects
