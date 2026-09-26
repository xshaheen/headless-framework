---
domain: Push Notifications
packages: PushNotifications.Abstractions, PushNotifications.Core, PushNotifications.Dev, PushNotifications.Firebase, PushNotifications.Apns
---

# Push Notifications

> Provider-agnostic push notification API with Firebase Cloud Messaging and Apple Push Notification service (APNs) for production and a no-op implementation for development, supporting an optional default service plus any number of named (keyed) instances.

## Orientation

Install `Headless.PushNotifications.Abstractions` plus one provider package. Register with `AddHeadlessPushNotifications(setup => setup.Use…())` — at most one **default** `Use*` provider per call (the default is optional; a named-only host is supported), plus any number of **named** services via `setup.AddNamed(name, i => i.Use…())`. Code against `IPushNotificationService` for the default; resolve named services with `IPushNotificationServiceProvider.GetService("name")` or `[FromKeyedServices("name")] IPushNotificationService`. Never reference provider-specific types in application code — swap providers by changing DI registration only.

```csharp
// Production — Firebase Cloud Messaging (default)
builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(builder.Configuration.GetSection("Firebase")));

// Production — iOS only, straight to Apple Push Notification service (no Firebase project)
builder.Services.AddHeadlessPushNotifications(setup => setup.UseApns(builder.Configuration.GetSection("Apns")));

// Development / testing — no-op, always succeeds
builder.Services.AddHeadlessPushNotifications(setup => setup.UseNoop());

// Default plus named instances (e.g. separate Firebase projects per app):
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase:Primary"));            // default (optional)
    setup.AddNamed("driver-app", i => i.UseFirebase(builder.Configuration.GetSection("Firebase:Driver")));
    setup.AddNamed("rider-app", i => i.UseFirebase(builder.Configuration.GetSection("Firebase:Rider")));
});

// Firebase for Android and Web, APNs direct for iOS:
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase"));                      // default
    setup.AddNamed("ios", i => i.UseApns(builder.Configuration.GetSection("Apns")));
});
```

`Headless.PushNotifications.Core` owns registration (`AddHeadlessPushNotifications`, `HeadlessPushNotificationsSetupBuilder`, `HeadlessPushNotificationsInstanceBuilder`) and the `IPushNotificationServiceProvider` implementation over keyed DI. Providers pull it transitively — you rarely install it directly. `Headless.PushNotifications.Abstractions` holds contracts only (`IPushNotificationService`, `IPushNotificationServiceProvider`, response types).

Use `IPushNotificationService.SendToDeviceAsync` for single-device delivery and `SendMulticastAsync` for batch sends. Always check `PushNotificationResponse.Status` — three states apply: `Success`, `Failure`, and `Unregistered`.

## Agent Rules

- Register at most one **default** provider per container: `services.AddHeadlessPushNotifications(setup => setup.Use…())`. The default is optional — zero defaults is allowed (a named-only host). Multiple default providers in one delegate, or a repeated `AddHeadlessPushNotifications` on the same `IServiceCollection`, throws `InvalidOperationException` at registration time. The available default `Use*` calls are `UseFirebase`, `UseApns`, and `UseNoop` — the same set is available on each named instance.
- Add **named** services in the same call: `setup.AddNamed("name", i => i.Use…())`. Names must be non-whitespace and ordinal-unique within the call, and each named instance must select exactly one provider — a duplicate name, whitespace name, or zero/multiple providers throws at registration time. The default is optional; a named-only host (no default) is supported — the unkeyed `IPushNotificationService` is simply not registered when no default is configured.
- Resolve a named service with `IPushNotificationServiceProvider.GetService("name")` (throws `InvalidOperationException` naming `AddNamed` when unregistered) / `GetServiceOrNull("name")` (returns `null`), or raw keyed DI (`[FromKeyedServices("name")] IPushNotificationService`, `GetRequiredKeyedService<IPushNotificationService>(name)`). Both `GetService` and `GetServiceOrNull` throw `ArgumentException` on a null/whitespace name. The default (unkeyed) `IPushNotificationService` is **not** exposed through `IPushNotificationServiceProvider`. To validate an externally supplied name before resolving, check `IPushNotificationServiceProvider.RegisteredNames` (the registered named-instance names, an `IReadOnlySet<string>`; the default is excluded) instead of probing `GetServiceOrNull` and handling `null`.
- Registration is deferred: provider contributions are queued and nothing touches the `IServiceCollection` until the gates pass, so a setup that throws leaves the collection unchanged. The same provider can back two different names with fully independent options.
- Each named Firebase instance isolates its own options (validated per name via FluentValidation + `ValidateOnStart`), its own retry pipeline (keyed `Headless:FcmRetry:{name}`), and its own lazily-created `FirebaseApp`. Keyed DI does not cascade the key to constructor dependencies, so named services never read the default configuration (a keyed sender reads `IOptionsMonitor.Get(name)`, never `CurrentValue`).
- Each named APNs instance likewise owns its options (validated per name at startup), its own HTTP client and resilience pipeline (named `Headless:Apns:{name}`), and its keyed service. It shares only the provider-token cache, which is keyed by team id and key id.
- Always code against `IPushNotificationService` from `Headless.PushNotifications.Abstractions`. Never reference `FcmPushNotificationService`, `ApnsPushNotificationService`, or other concrete types in application code.
- Use `Headless.PushNotifications.Dev` (`UseNoop()`) in development and testing environments to avoid sending real notifications. Switch on `builder.Environment.IsDevelopment()`.
- Do NOT call the Firebase Admin SDK (`FirebaseAdmin`, `FirebaseMessaging`) directly. Route all sends through `IPushNotificationService`.
- After every send, check `PushNotificationResponse.Status`. Three distinct states exist — `Success`, `Failure`, and `Unregistered` — and `IsSucceeded()` / `IsFailed()` both return `false` for an unregistered token. Use `IsUnregistered()` explicitly and remove that token from your store.
- FCM enforces content limits: **title ≤ 100 characters**, **body ≤ 4 000 characters**. `FcmPushNotificationService` throws `ArgumentException` if either limit is exceeded.
- FCM data payload keys `from`, `notification`, `message_type`, and any key starting with `google` or `gcm` are reserved. Passing a reserved key throws `ArgumentException` from `SendToDeviceAsync` / `SendMulticastAsync` before any network call.
- A single `SendMulticastAsync` call handles any number of tokens. Firebase chunks them into batches of at most 500 (the FCM hard limit); APNs has no batch endpoint, so it sends one request per token with at most `ApnsOptions.MaxConcurrency` in flight.
- Firebase retries transient errors (HTTP 429 `QuotaExceeded`, 503 `Unavailable`, 500 `Internal`, network errors, non-user timeouts) automatically with exponential backoff. Do not wrap calls in your own retry for these errors.
- Firebase does not retry permanent errors (`Unregistered`, `InvalidArgument`, `SenderIdMismatch`, `ThirdPartyAuthError`). `Unregistered` is returned as `PushNotificationResponseStatus.Unregistered`, not as a failure.
- Do not disable Firebase retry in production (`MaxAttempts = 0`) unless you have your own resilience infrastructure. Default is 5 attempts with exponential backoff and jitter.
- `FirebaseOptions.Json` contains sensitive private-key material. Do not log it, serialize it, or store it in configuration as plain text in production. The `ToString()` override on `FirebaseOptions` redacts it.
- `PushNotificationRequest.CollapseKey` is limited to 64 UTF-8 bytes by both production providers, because Apple caps the `apns-collapse-id` header at 64 bytes. A longer key throws `ArgumentException` before any network call.
- APNs reports a token as `Unregistered` only on HTTP 410. `BadDeviceToken` (HTTP 400) is a `Failure` by default, because Apple returns it both for a malformed token and for a valid token sent to the wrong environment. A host pointed at the wrong `ApnsOptions.Environment` would discard every valid token it holds if that rejection meant unregistered. Set `TreatBadDeviceTokenAsUnregistered = true` only when the environment is known to be right.
- APNs payloads are limited to 4096 bytes (5120 bytes when `PushType = ApnsPushType.Voip`), measured on the JSON the provider writes, including `Data`. An oversized payload throws `ArgumentException` before any network call.
- APNs reserves the top-level `aps` key. A `Data` entry named `aps` throws `ArgumentException`. Every other `Data` key is written at the top level of the payload, beside `aps`.
- Load `ApnsOptions.PrivateKey` (the `.p8` PEM text) from a secret store or an environment variable, never from committed configuration. The appsettings sample in this guide uses a placeholder. `ToString()` redacts the key and `[JsonIgnore]` keeps it out of serialized options.
- Rotating the APNs signing key requires a process restart: the provider caches the imported key and its provider token per `(team id, key id)` for the life of the container. Two option sets with the same team id and key id but different key text are refused, and every send through the second one returns `Failure`.
- Every process that signs with the same APNs key mints its own provider token, and Apple rejects token updates for one key more often than once every 20 minutes with `TooManyProviderTokenUpdates`. Within one container the provider shares one token per key. Across many processes or hosts, prefer a separate key per environment or deployment.
- APNs retries in-process only connection failures that happen before a request is sent (connect, DNS, TLS), at most twice. A connection lost after a request was sent is not retried, because APNs does not deduplicate and a resend could show the notification twice; it becomes a `Failure` you may retry at the risk of a duplicate. An HTTP 5xx is never retried in-process and becomes a `Failure`: Apple asks senders to wait about 15 minutes before retrying one, so retry it later from your own queue or job rather than in a tight loop. Never retry an HTTP 429 `TooManyRequests` immediately: it throttles that one device token.

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
| `Success` | `true` | `false` | `false` | Accepted by the provider; `MessageId` is non-null |
| `Failure` | `false` | `true` | `false` | Provider rejected the message; `FailureError` is non-null |
| `Unregistered` | `false` | `false` | `true` | Token is no longer valid; remove from your store |

The `Unregistered` state is not a failure — it is a signal to clean up stale tokens. Firebase reports it for an FCM `Unregistered` error; APNs reports it for HTTP 410 (and, when opted in, for `BadDeviceToken`). A simple `if (!response.IsSucceeded())` will miss the unregistered case. In a `BatchPushNotificationResponse`, an unregistered token counts toward `FailureCount`, because that count is every response that did not succeed.

### Provider Selection Model

`AddHeadlessPushNotifications` accepts a callback that receives a `HeadlessPushNotificationsSetupBuilder`. Provider packages each contribute a `Use{Provider}` C# 14 extension member on that builder (default slot) and on `HeadlessPushNotificationsInstanceBuilder` (named slot). The framework enforces **at most one default** provider per registration, plus unbounded named instances — this prevents accidental dual-registration of the default while making per-instance routing explicit. Providers contribute deferred `Action<IServiceCollection>` registrations rather than implementing a provider-options interface, keeping the default and named paths symmetric.

### Multicast and Batching

`SendMulticastAsync` sends the same notification to many device tokens and aggregates the results into a single `BatchPushNotificationResponse`. The `Responses` list has exactly one entry per input token, preserving input order. Providers surface transport failures that remain after retries as `Failure` outcomes rather than throwing, so results already collected are never discarded; only invalid input and caller cancellation throw.

- **Firebase** chunks token lists into batches of ≤ 500 (the FCM limit). A whole-batch transport failure after all retries becomes a `Failure` for every token in that batch.
- **APNs** has no batch endpoint. It sends one HTTP/2 request per token, with at most `ApnsOptions.MaxConcurrency` (default 100) in flight, and validates every token before the first send, so one blank token cannot cause a partial delivery. A transport failure affects only the token it hit.

## Choosing a Provider

| | Firebase (`Headless.PushNotifications.Firebase`) | APNs (`Headless.PushNotifications.Apns`) | Dev no-op (`Headless.PushNotifications.Dev`) |
|---|---|---|---|
| **Use when** | Production mobile apps (Android, iOS via APNs bridge, Web) | Production iOS apps that should not depend on a Firebase project, or need VoIP pushes | Local development, test suites, CI |
| **Avoid when** | Local development (real credentials, real sends) | Android or Web clients; background or live-activity pushes; local development | Any production environment |
| **Backend** | Firebase Cloud Messaging (FCM v1 API via `FirebaseAdmin`) | Apple Push Notification service over HTTP/2, called directly | In-process stub |
| **Credentials** | Firebase service account JSON (`FirebaseOptions.Json`) | APNs `.p8` signing key plus key id, team id, and bundle id | None |
| **Retry** | Automatic exponential backoff for transient FCM errors | At most 2 short retries for pre-send connection failures; a 5xx is a `Failure`, and the caller retries it after about 15 minutes | N/A |
| **Trade-off** | Requires a Firebase project and service account | iOS only; one request per token (no batch endpoint); key rotation needs a restart | Zero external dependencies; always succeeds |

---
## Headless.PushNotifications.Abstractions

Defines the unified interface and contract types for push notification services.

### API and behavior

- `IPushNotificationService` — core sending interface:
  - `SendToDeviceAsync(clientToken, request, ct)` — single-device delivery
  - `SendMulticastAsync(clientTokens, request, ct)` — batch delivery
- `PushNotificationRequest` — the notification payload (`required Title`, `required Body`, optional `Data`, optional `CollapseKey`). Both send methods take one request; add new delivery options as optional `init` properties rather than new overloads.
  - `CollapseKey` groups notifications so a newer one replaces an older undelivered one with the same key on the device. `null` (the default) disables collapsing. Each provider maps and limits it: APNs sends it as the `apns-collapse-id` header, and Firebase sends it as the Android collapse key and as the same `apns-collapse-id` header through its iOS bridge. Both reject a key over 64 UTF-8 bytes with `ArgumentException`.
- `IPushNotificationServiceProvider` — resolves named services by name: `GetService(name)` (throws when unregistered) and `GetServiceOrNull(name)` (returns `null`), plus `RegisteredNames` (`IReadOnlySet<string>`) listing the registered named instances (the default is excluded) so an externally supplied name can be validated before resolving. Backed by the container's keyed `IPushNotificationService` registrations; the concrete implementation lives in `Headless.PushNotifications.Core`.
- `PushNotificationResponse` — single-device outcome with three states (`Success`, `Failure`, `Unregistered`); factory methods `Succeeded`, `Failed`, `Unregistered`; query methods `IsSucceeded()`, `IsFailed()`, `IsUnregistered()`; properties `Token`, `MessageId?`, `FailureError?`, `Status`
- `PushNotificationResponseStatus` enum — `Success`, `Failure`, `Unregistered`
- `BatchPushNotificationResponse` — multicast aggregate: `SuccessCount`, `FailureCount`, `Responses` (one per token)

### Install

```bash
dotnet add package Headless.PushNotifications.Abstractions
```

### Setup and use

```csharp
public sealed class NotificationService(IPushNotificationService pushService, ILogger<NotificationService> logger)
{
    public async Task SendAsync(string deviceToken, string title, string message, CancellationToken ct)
    {
        var response = await pushService.SendToDeviceAsync(
            deviceToken,
            new PushNotificationRequest
            {
                Title = title,
                Body = message,
                Data = new Dictionary<string, string> { ["orderId"] = "123" },
            },
            ct
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
        var result = await pushService.SendMulticastAsync(
            tokens,
            new PushNotificationRequest { Title = title, Body = message },
            ct
        );

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

### Runtime behavior

None. This package defines only interfaces and contracts.
---
## Headless.PushNotifications.Core

Setup builder, registration gates, and the named-service provider for the push-notifications abstraction.

### API and behavior

- `AddHeadlessPushNotifications(Action<HeadlessPushNotificationsSetupBuilder>)` — the single provider-agnostic registration entry point, with an at-most-one-default-provider gate and a once-per-collection guard.
- `HeadlessPushNotificationsSetupBuilder` — receives the optional default `Use*` selection plus `AddNamed(name, …)` named instances; `HeadlessPushNotificationsInstanceBuilder` — the per-named-instance builder that providers extend with their `Use*` members.
- `IPushNotificationServiceProvider` — registered automatically by the gate (keyed-service-backed via `KeyedServicePushNotificationServiceProvider`); resolves named services by name and exposes `RegisteredNames` (the registered named instances, default excluded) for validating a name before resolving.
- Deferred registration: provider contributions are queued and run only after the gates pass — the default first, then each named instance — so a setup that fails a gate leaves the `IServiceCollection` unchanged.

### Design constraints

The builder carries no shared, cross-provider feature options — it is provider-selection-only; each provider binds its own options inside its `Use*` member. The gate is **per-slot**: it allows at most one default provider (rejecting a second, but permitting zero for a named-only host) while allowing unbounded ordinal-unique named instances, and rejects a repeated `AddHeadlessPushNotifications` on the same `IServiceCollection` (a marker service enforces the single-call rule). Providers contribute deferred `Action<IServiceCollection>` registrations (`RegisterDefaultProvider` for the default, `instance.RegisterProvider` for a named instance) rather than implementing a provider interface, keeping the default and named paths symmetric. `IPushNotificationServiceProvider` resolves only named (keyed) services — the default service, when configured, is the unkeyed `IPushNotificationService`, reachable directly and never by name — and `IPushNotificationServiceProvider.RegisteredNames` enumerates the named instances.

### Install

```bash
dotnet add package Headless.PushNotifications.Core
```

### Setup and use

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

### Runtime behavior

`AddHeadlessPushNotifications` registers a provider-registration marker and `IPushNotificationServiceProvider` (keyed-service-backed), then runs the default provider's wiring (the unkeyed `IPushNotificationService`) when a default is configured, followed by each named instance's wiring (keyed under the instance name). The marker enforces the single-call rule.
---
## Headless.PushNotifications.Dev

No-op push notification provider for local development and testing.

### API and behavior

- Silent `IPushNotificationService` implementation (`NoopPushNotificationService`)
- No network calls or external dependencies
- Always returns `Success` responses with a generated GUID as the message id
- Never validates input or throws (inert for any caller, including invalid tokens or empty titles)
- Selectable as the default (`setup.UseNoop()`) or as a named instance (`setup.AddNamed("name", i => i.UseNoop())`)

### Install

```bash
dotnet add package Headless.PushNotifications.Dev
```

### Setup and use

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

### Runtime behavior

- Registers `IPushNotificationService` as singleton (`NoopPushNotificationService`) for the default, or a keyed singleton under the instance name for a named instance
---
## Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation of `IPushNotificationService` for production push notifications.

### API and behavior

- FCM-backed `IPushNotificationService` implementation (`FcmPushNotificationService`)
- Selectable as the default (`setup.UseFirebase(…)`) or as a named instance (`setup.AddNamed("name", i => i.UseFirebase(…))`), each isolating its own options, retry pipeline, and `FirebaseApp`
- Single-device (`SendToDeviceAsync`) and multicast (`SendMulticastAsync`) delivery
- Automatic chunking of multicast sends into batches of ≤ 500 tokens (FCM hard limit)
- Custom data payload support (with reserved-key enforcement)
- Input validation: title ≤ 100 characters, body ≤ 4 000 characters, `CollapseKey` ≤ 64 UTF-8 bytes
- `CollapseKey` is sent as the Android collapse key and as the `apns-collapse-id` header of the APNs bridge
- **Automatic retry** for transient failures: exponential backoff with jitter, Retry-After header support for rate limits
- Configurable retry policy (`FirebaseRetryOptions`): `MaxAttempts` (0–10), `MaxDelay`, `RateLimitDelay`, `UseJitter`
- Structured logging and OpenTelemetry Activity events on retry
- Options validated at startup via FluentValidation

### Design constraints

The Firebase Admin SDK `FirebaseApp` is created **lazily on the first send**, not at DI registration time. This means:
- Registration has no observable side effects (no credentials are loaded, no HTTP calls are made).
- Multiple hosts (default plus named instances) in the same process coexist with different credentials — each registration generates a uniquely-named `FirebaseApp`.
- Configuration errors in `FirebaseOptions.Json` (malformed JSON, wrong credential type) surface as exceptions on the first call, not at startup. Supply the `IConfiguration` overload so the options validator catches missing `Json` at startup instead.

Each named instance reads its own options snapshot (`IOptionsMonitor<FirebaseOptions>.Get(name)`) and its own retry pipeline (keyed `Headless:FcmRetry:{name}`); the default reads the unnamed options and the `Headless:FcmRetry` pipeline. Keyed DI does not cascade the key to constructor dependencies, so a keyed sender never reads `CurrentValue` (which binds the default) — the sender is registered through an explicit factory that passes its own name.

Android messages are sent with `Priority.High`; iOS messages include an APNs badge count of 1. These are hardcoded defaults — the `data` payload provides the only customization surface exposed by this abstraction.

### Install

```bash
dotnet add package Headless.PushNotifications.Firebase
```

### Setup and use

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
    deviceToken,
    new PushNotificationRequest
    {
        Title = "Order shipped",
        Body = "Your order #1234 is on its way.",
        Data = new Dictionary<string, string> { ["orderId"] = "1234" },
    },
    ct
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

### Runtime behavior

- Registers `IPushNotificationService` as singleton (`FcmPushNotificationService`) for the default, or a keyed singleton under the instance name for a named instance
- Registers a `ResiliencePipeline` named `"Headless:FcmRetry"` (default) or `"Headless:FcmRetry:{name}"` (per named instance) via Polly
- Registers `TimeProvider.System` as singleton (if not already registered)
- The Firebase Admin SDK `FirebaseApp` is created lazily on first send; registration has no network side effects
---
## Headless.PushNotifications.Apns

Apple Push Notification service (APNs) implementation of `IPushNotificationService` for iOS push notifications without a Firebase project.

### API and behavior

- APNs-backed `IPushNotificationService` implementation (`ApnsPushNotificationService`) using token-based (`.p8` key) authentication over HTTP/2
- Selectable as the default (`setup.UseApns(…)`) or as a named instance (`setup.AddNamed("name", i => i.UseApns(…))`). Each slot has four overloads: `IConfiguration`, `Action<ApnsOptions>`, `Action<ApnsOptions, IServiceProvider>`, and a pre-built `ApnsOptions`. Every overload also takes an optional `configureClient` (`Action<HttpClient>`, run after the environment's `BaseAddress` is set, so it may replace it) and an optional `configureResilience` (`Action<HttpStandardResilienceOptions>`)
- Single-device (`SendToDeviceAsync`) and multicast (`SendMulticastAsync`) delivery; a multicast sends one request per token with at most `MaxConcurrency` in flight and returns responses in input order
- Payload shape: `{"aps":{"alert":{"title":…,"body":…}}, …}`, with each `Data` entry written as a top-level string field beside `aps`
- `CollapseKey` is sent as the `apns-collapse-id` header
- A success carries a client-generated UUID, sent as the `apns-id` header, as `MessageId`
- Alert and VoIP push types, configurable priority, production and sandbox environments
- Options validated at startup via FluentValidation and `ValidateOnStart`

Input validation throws `ArgumentException` before any network call when:

- the device token is null, empty, or whitespace, or any token in a multicast is (a multicast checks every token before the first send)
- the multicast token list is null or empty
- the request is null, or its title or body is blank
- `Data` contains the reserved key `aps`
- `CollapseKey` exceeds 64 UTF-8 bytes
- the written JSON payload exceeds 4096 bytes (5120 bytes for `ApnsPushType.Voip`)

### Design constraints

- **Provider tokens are shared per key.** The provider signs one ES256 provider token per `(team id, key id)` for the whole container and refreshes it every 50 minutes. The default and every named instance that use the same key share that token, because Apple rejects token updates for one key more often than once every 20 minutes (`TooManyProviderTokenUpdates`). Two option sets with the same team id and key id but different `PrivateKey` text are refused: every send through the later one returns `Failure`. Separate processes cannot share the token, so many processes signing with one key can still hit `TooManyProviderTokenUpdates`; prefer a separate key per environment or deployment.
- **Key rotation needs a restart.** The imported key and its token stay cached for the life of the container. A changed `PrivateKey` in reloaded configuration is refused as a different key for the same identity.
- **HTTP/2 only.** Requests require HTTP/2 exactly (`HttpVersionPolicy.RequestVersionExact`). One long-lived connection pool serves each instance (6-hour connection lifetime, hourly keep-alive ping), because Apple asks senders to keep connections open instead of reconnecting per notification.
- **Device tokens stay out of logs.** The `HttpClientFactory` request loggers are removed, because the request URI carries the raw device token. The provider's own log messages mask the token to its first 8 characters.
- **HTTPS only, except loopback.** A non-HTTPS endpoint is refused unless its host is loopback, so a `configureClient` override cannot send the bearer token in cleartext over a network. The refusal surfaces as a `Failure` on each send.
- **Environment must match the token.** A device token belongs to the environment the app was built for. `Sandbox` is for builds signed with a development profile; `Production` (the default) is for App Store, TestFlight, and ad hoc builds.
- **Current limitations.** Only alert and VoIP push types are supported: background, live-activity, and other push types are not. Priority and push type are set per instance in `ApnsOptions`, not per message, and no expiration (`apns-expiration`) is sent or configurable. Certificate (`.p12`) authentication is not supported.

### Install

```bash
dotnet add package Headless.PushNotifications.Apns
```

### Setup and use

```csharp
var builder = WebApplication.CreateBuilder(args);

// Bind from configuration so the options validator runs at startup.
// Supply the key from a secret store, e.g. the Apns__PrivateKey environment variable.
builder.Services.AddHeadlessPushNotifications(setup => setup.UseApns(builder.Configuration.GetSection("Apns")));

// Firebase for Android and Web as the default, APNs direct for iOS:
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase"));
    setup.AddNamed("ios", i => i.UseApns(builder.Configuration.GetSection("Apns")));
});

// A VoIP instance beside the alert instance, sharing one key and so one provider token:
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseApns(builder.Configuration.GetSection("Apns"));
    setup.AddNamed(
        "voip",
        i => i.UseApns(options =>
        {
            builder.Configuration.GetSection("Apns").Bind(options);
            options.PushType = ApnsPushType.Voip;
        })
    );
});
```

Sending:

```csharp
var response = await pushService.SendToDeviceAsync(
    deviceToken,
    new PushNotificationRequest
    {
        Title = "Order shipped",
        Body = "Your order #1234 is on its way.",
        Data = new Dictionary<string, string> { ["orderId"] = "1234" },
        CollapseKey = "order-1234",
    },
    ct
);

if (response.IsUnregistered())
    await tokenStore.RemoveAsync(deviceToken, ct);
```

### Configuration

#### appsettings.json

```json
{
  "Apns": {
    "KeyId": "ABC123DEFG",
    "TeamId": "DEF123GHIJ",
    "PrivateKey": "<set from a secret store, not committed configuration>",
    "BundleId": "com.example.app",
    "Environment": "Production",
    "PushType": "Alert",
    "Priority": "Immediate",
    "TreatBadDeviceTokenAsUnregistered": false,
    "MaxConcurrency": 100
  }
}
```

#### ApnsOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `KeyId` | `string` | _(required)_ | 10-character key identifier (ASCII letters and digits) of the APNs signing key. |
| `TeamId` | `string` | _(required)_ | 10-character Apple Developer team identifier that owns the key. |
| `PrivateKey` | `string` | _(required)_ | PEM text of the `AuthKey_*.p8` file, a P-256 EC private key. `ToString()` redacts it and `[JsonIgnore]` excludes it from serialization. |
| `BundleId` | `string` | _(required)_ | App bundle identifier, sent as the `apns-topic` header. VoIP pushes use `<BundleId>.voip`. |
| `Environment` | `ApnsEnvironment` | `Production` | `Production` (`api.push.apple.com`) or `Sandbox` (`api.sandbox.push.apple.com`). |
| `PushType` | `ApnsPushType` | `Alert` | `Alert` or `Voip`. `Voip` changes the topic to `<BundleId>.voip` and raises the payload limit to 5120 bytes. |
| `Priority` | `ApnsPriority` | `Immediate` | `Immediate` (10), `PowerConsiderate` (5), or `PowerPrioritized` (1), sent as `apns-priority`. |
| `TreatBadDeviceTokenAsUnregistered` | `bool` | `false` | Report HTTP 400 `BadDeviceToken` as `Unregistered` instead of `Failure`. Leave off unless the environment is known to be right. |
| `MaxConcurrency` | `int` | `100` | Maximum requests in flight during one multicast. Range 1–1000. |

#### Resilience overrides

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseApns(
        builder.Configuration.GetSection("Apns"),
        configureResilience: options => options.Retry.MaxRetryAttempts = 1
    )
);
```

### Runtime behavior

#### Status mapping

| APNs answer | Result |
|---|---|
| HTTP 200 | `Success`; `MessageId` is the `apns-id` the provider generated |
| HTTP 410 (any reason) | `Unregistered` |
| HTTP 400 `BadDeviceToken` | `Failure`, or `Unregistered` when `TreatBadDeviceTokenAsUnregistered` is `true` |
| HTTP 403 `ExpiredProviderToken` | One token re-mint and one retry of the send with the new token. The token is never re-minted within 20 minutes of the last mint; in that window the send is not repeated and the rejection is a `Failure`. A second rejection is a `Failure` |
| Any other rejection, including `DeviceTokenNotForTopic` | `Failure` with `FailureError` `"<reason> (HTTP <status>)"`, or `"APNs rejected the request (HTTP <status>)"` when the body has no reason |
| HTTP 5xx | `Failure`, without an in-process retry. Apple asks senders to wait about 15 minutes before retrying it |
| Transport fault after retries, open circuit breaker, rate-limiter rejection, timeout, refused endpoint | `Failure` with `FailureError` `"<ExceptionType>: <message>"` |
| Caller cancellation | Throws `OperationCanceledException` |

#### Retry policy

The provider registers the standard `Microsoft.Extensions.Http.Resilience` handler on its HTTP client (named `Headless:Apns`, or `Headless:Apns:{name}` for a named instance), with these changes:

- Retries only an `HttpRequestException` whose `HttpRequestError` is `ConnectionError`, `NameResolutionError`, or `SecureConnectionError` (the request never reached APNs), at most 2 times. A connection lost after the request was sent is not retried: APNs does not deduplicate, so a resend could deliver the notification twice.
- Never retries an HTTP 5xx. It becomes a `Failure`, because Apple asks senders to wait about 15 minutes before retrying one; retry it from your own queue or job.
- Never retries HTTP 429 `TooManyRequests`, which throttles one device token.
- The circuit breaker counts the retried connection failures, HTTP 500, HTTP 503, and attempt timeouts, not 429, so throttled tokens cannot open the breaker for the whole instance.
- The concurrency limiter queues up to 10 000 requests, so concurrent multicasts wait for a slot instead of being rejected.

Pass `configureResilience` to change any of these.

#### Registrations

- Registers `IPushNotificationService` as a singleton for the default, or a keyed singleton under the instance name for a named instance
- Registers one container-wide provider-token cache, and `TimeProvider.System` as a singleton if not already registered
- Registration has no network side effects; the signing key is loaded into the token cache and the first provider token minted on the first send
