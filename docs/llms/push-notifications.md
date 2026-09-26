---
domain: Push Notifications
packages: PushNotifications.Abstractions, PushNotifications.Core, PushNotifications.Dev, PushNotifications.Firebase, PushNotifications.Apns
---

# Push Notifications

> Provider-agnostic push notification API with Firebase Cloud Messaging and Apple Push Notification service (APNs) for production and a no-op implementation for development, supporting an optional default service plus any number of named (keyed) instances.

## Orientation

Install `Headless.PushNotifications.Abstractions` plus one provider package. Register with `AddHeadlessPushNotifications(setup => setup.Use…())` — at most one **default** `Use*` provider per call (the default is optional; a named-only host is supported), plus any number of **named** services via `setup.AddNamed(name, i => i.Use…())`. Code against `IPushNotificationService` for the default; resolve named services with `IPushNotificationServiceProvider.GetService("name")` or `[FromKeyedServices("name")] IPushNotificationService`. Never reference provider-specific types in application code — swap providers by changing DI registration only. The one exception is `IApnsPushNotificationService`, for pushes only APNs can send.

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

A request is either a notification (title and body) or a data-only message (data, no title or body). Badge, sound, priority, and time-to-live are optional on the shared request, and each provider maps them (see [Shared delivery fields](#shared-delivery-fields)).

For pushes only APNs has — Live Activities, rich alert controls, background pushes with APNs details, location, push-to-talk, widgets, controls, complications, and File Provider — inject `IApnsPushNotificationService` from `Headless.PushNotifications.Apns`. It resolves wherever the APNs `IPushNotificationService` does, and returns the APNs status, reason, and ids with each result.

## Agent Rules

- Register at most one **default** provider per container: `services.AddHeadlessPushNotifications(setup => setup.Use…())`. The default is optional — zero defaults is allowed (a named-only host). Multiple default providers in one delegate, or a repeated `AddHeadlessPushNotifications` on the same `IServiceCollection`, throws `InvalidOperationException` at registration time. The available default `Use*` calls are `UseFirebase`, `UseApns`, and `UseNoop` — the same set is available on each named instance.
- Add **named** services in the same call: `setup.AddNamed("name", i => i.Use…())`. Names must be non-whitespace and ordinal-unique within the call, and each named instance must select exactly one provider — a duplicate name, whitespace name, or zero/multiple providers throws at registration time. The default is optional; a named-only host (no default) is supported — the unkeyed `IPushNotificationService` is simply not registered when no default is configured.
- Resolve a named service with `IPushNotificationServiceProvider.GetService("name")` (throws `InvalidOperationException` naming `AddNamed` when unregistered) / `GetServiceOrNull("name")` (returns `null`), or raw keyed DI (`[FromKeyedServices("name")] IPushNotificationService`, `GetRequiredKeyedService<IPushNotificationService>(name)`). Both `GetService` and `GetServiceOrNull` throw `ArgumentException` on a null/whitespace name. The default (unkeyed) `IPushNotificationService` is **not** exposed through `IPushNotificationServiceProvider`. To validate an externally supplied name before resolving, check `IPushNotificationServiceProvider.RegisteredNames` (the registered named-instance names, an `IReadOnlySet<string>`; the default is excluded) instead of probing `GetServiceOrNull` and handling `null`.
- Registration is deferred: provider contributions are queued and nothing touches the `IServiceCollection` until the gates pass, so a setup that throws leaves the collection unchanged. The same provider can back two different names with fully independent options.
- Each named Firebase instance isolates its own options (validated per name via FluentValidation + `ValidateOnStart`), its own retry pipeline (keyed `Headless:FcmRetry:{name}`), and its own lazily-created `FirebaseApp`. Keyed DI does not cascade the key to constructor dependencies, so named services never read the default configuration (a keyed sender reads `IOptionsMonitor.Get(name)`, never `CurrentValue`).
- Each named APNs instance likewise owns its options (validated per name at startup), its own HTTP client and resilience pipeline (named `Headless:Apns:{name}`), and its keyed service. It shares only the provider-token cache, which is keyed by team id and key id.
- Always code against `IPushNotificationService` from `Headless.PushNotifications.Abstractions`. Never reference `FcmPushNotificationService` or other concrete types in application code. The one provider-specific interface is `IApnsPushNotificationService`: take it only for APNs-only push types or APNs response details, and keep plain alerts and data-only messages on `IPushNotificationService` so the provider stays swappable.
- A `PushNotificationRequest` is a notification (non-blank `Title` and `Body`) or a data-only message (no `Title`, no `Body`, at least one `Data` entry, no `Badge`, no `Sound`). Every other combination, a negative `Badge` or `TimeToLive`, a `TimeToLive` over 28 days, a blank `Sound`, or an undefined `Priority` throws `ArgumentException` before any network call. `UseNoop()` does not validate.
- A data-only message is a background push. iOS throttles background pushes and may drop them, so never rely on one arriving.
- On APNs, a data-only message always goes at priority 5, whatever `Priority` says, because Apple requires 5 for background pushes. A VoIP-configured instance is the exception: it sends a data-only message as a VoIP push and honors `Priority`.
- `Badge = 0` clears the badge on iOS. Android cannot clear a badge, so Firebase sends no Android count for 0.
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
- APNs payloads are limited to 4096 bytes for every push type except VoIP, which allows 5120 bytes. The limit is measured on the JSON the provider writes, including `Data`. An oversized payload throws `ArgumentException` before any network call.
- APNs reserves the top-level `aps` key. A `Data` entry named `aps` throws `ArgumentException`. Every other `Data` key is written at the top level of the payload, beside `aps`, never inside it: APNs ignores custom keys in `aps`. The typed notifications take `Data` as a `JsonObject`, so a value can be any JSON; the shared `PushNotificationRequest.Data` stays a string map, because FCM data messages carry only strings.
- An instance configured with `PushType = ApnsPushType.Voip` holds PushKit tokens, which receive only VoIP pushes. Through it, `IApnsPushNotificationService` sends only `ApnsAlertNotification` and an `ApnsRawNotification` of type `Alert` or `Voip` (each as a VoIP push); every other notification type throws `ArgumentException`.
- Live Activity pushes default to priority 5, while Apple's default is 10, so an update can be delayed. Set `Priority = ApnsPriority.Immediate` only for updates the user must see now: Apple budgets priority-10 Live Activity pushes per hour.
- Send a Live Activity `Start` to the app's push-to-start token, and `Update` and `End` to the push token of the running activity. These are not the device token used for alerts. Set `RequestPushToken` on the `Start` so the started activity reports that push token (iOS 18 and later).
- `ApnsNotification.ApnsId` sets the `apns-id` for a single-token `SendAsync`. A multicast with `ApnsId` set throws `ArgumentException`, because every request needs its own id.
- A critical sound (`ApnsSound.Critical`) or `ApnsInterruptionLevel.Critical` needs Apple's critical-alerts entitlement (`com.apple.developer.usernotifications.critical-alerts`) on the app. Without it the device plays a critical sound as an ordinary sound. The provider does not check the entitlement.
- Load `ApnsOptions.PrivateKey` (the `.p8` PEM text), `Certificate`, and `CertificatePassword` from a secret store or an environment variable, never from committed configuration. The appsettings sample in this guide uses a placeholder. `ToString()` redacts all three and `[JsonIgnore]` keeps them out of serialized options.
- An APNs instance authenticates with a `.p8` signing key (token mode) or a `.p12` provider certificate (certificate mode), never both. Certificate mode refuses `location`, `fileprovider`, `liveactivity`, `widgets`, and `controls` pushes with `ArgumentException`, whether typed or sent as an `ApnsRawNotification` of that `Type`. Use token mode for those.
- Rotating the APNs signing key requires a process restart: the provider caches the imported key and its provider token per `(team id, key id)` for the life of the container. Two option sets with the same team id and key id but different key text are refused, and every send through the second one returns `Failure`. A renewed certificate does not: when the configuration source reloads, new connections present it (see [Certificate authentication](#certificate-authentication)).
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

### Notification and data-only requests

A `PushNotificationRequest` is one of two kinds:

| Kind | `Title` and `Body` | `Data` | `Badge`, `Sound` | What the device does |
|---|---|---|---|---|
| Notification | Both non-blank | Optional | Optional | Shows the notification |
| Data-only | Both `null` | At least one entry | Not allowed | Wakes the app in the background and shows nothing |

Setting only one of `Title` and `Body` makes the request a notification, so the other must be non-blank too. Firebase and APNs reject any other combination with `ArgumentException` before any network call.

```csharp
// Silent sync signal: no title, no body, data only.
await pushService.SendToDeviceAsync(
    deviceToken,
    new PushNotificationRequest { Data = new Dictionary<string, string> { ["sync"] = "orders" } },
    ct
);
```

### Shared delivery fields

`null` means "not set" for every field.

| Field | APNs provider | Firebase: Android | Firebase: APNs bridge |
|---|---|---|---|
| `Badge` (`int?`, not negative) | `aps.badge`; `0` clears the badge | Notification count; `0` is not sent, because Android cannot clear a badge | `aps.badge` |
| `Sound` (`string?`, a bundled sound name or `default`) | `aps.sound` | Notification sound | `aps.sound` |
| `Priority` (`PushNotificationPriority?`) | `High` sends `apns-priority: 10`, `Normal` sends `5`. `null` uses `ApnsOptions.Priority` | `High` or `Normal` message priority. `null` sends high | `High` sends `apns-priority: 10`, `Normal` sends `5`. `null` sends no header |
| `TimeToLive` (`TimeSpan?`, 0 to 28 days) | `apns-expiration` = now + TTL in epoch seconds. `TimeSpan.Zero` sends `0`: one attempt, no storage | Message TTL | `apns-expiration`, computed the same way |
| `CollapseKey` (`string?`, ≤ 64 UTF-8 bytes) | `apns-collapse-id` | Collapse key | `apns-collapse-id` |

A `null` `TimeToLive` sends no expiration, so the provider's own storage policy applies. The exception is an APNs VoIP instance, which sends `apns-expiration: 0`, because Apple tells senders to deliver VoIP pushes once or within seconds. A `TimeToLive` over 28 days, Firebase's maximum Android TTL, throws `ArgumentOutOfRangeException` on every provider. An unset `Badge` sends no badge on either provider.

A data-only request maps as follows:

- **APNs provider.** On an alert instance it becomes a background push: `{"aps":{"content-available":1}}` plus `Data`, push type `background`, always priority 5. On a VoIP instance it becomes a VoIP push, `{"aps":{}}` plus `Data`, with the request's `Priority` or `ApnsOptions.Priority`. PushKit never receives a `background` push.
- **Firebase.** No notification block. The Android message priority follows `Priority` (high when `null`). The APNs bridge gets `aps.content-available: 1` and `apns-priority: 5`, whatever `Priority` says.

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
| **Use when** | Production mobile apps (Android, iOS via APNs bridge, Web) | Production iOS apps that should not depend on a Firebase project, or need VoIP, Live Activity, or other APNs-only pushes | Local development, test suites, CI |
| **Avoid when** | Local development (real credentials, real sends) | Android or Web clients; local development | Any production environment |
| **Backend** | Firebase Cloud Messaging (FCM v1 API via `FirebaseAdmin`) | Apple Push Notification service over HTTP/2, called directly | In-process stub |
| **Credentials** | Firebase service account JSON (`FirebaseOptions.Json`) | APNs `.p8` signing key plus key id and team id, or a `.p12` provider certificate; plus the bundle id | None |
| **Retry** | Automatic exponential backoff for transient FCM errors | At most 2 short retries for pre-send connection failures; a 5xx is a `Failure`, and the caller retries it after about 15 minutes | N/A |
| **Trade-off** | Requires a Firebase project and service account | iOS only; one request per token (no batch endpoint); key rotation needs a restart | Zero external dependencies; always succeeds |

---
## Headless.PushNotifications.Abstractions

Defines the unified interface and contract types for push notification services.

### API and behavior

- `IPushNotificationService` — core sending interface:
  - `SendToDeviceAsync(clientToken, request, ct)` — single-device delivery
  - `SendMulticastAsync(clientTokens, request, ct)` — batch delivery
- `PushNotificationRequest` — the notification payload: `Title`, `Body`, `Data`, `CollapseKey`, `Badge`, `Sound`, `Priority`, and `TimeToLive`, all optional `init` properties where `null` means "not set". A request is a notification or a data-only message (see [Notification and data-only requests](#notification-and-data-only-requests)); [Shared delivery fields](#shared-delivery-fields) shows how each provider maps the fields. Both send methods take one request; add new delivery options as optional `init` properties rather than new overloads.
  - `CollapseKey` groups notifications so a newer one replaces an older undelivered one with the same key on the device. `null` (the default) disables collapsing. Each provider maps and limits it: APNs sends it as the `apns-collapse-id` header, and Firebase sends it as the Android collapse key and as the same `apns-collapse-id` header through its iOS bridge. Both reject a key over 64 UTF-8 bytes with `ArgumentException`.
- `IPushNotificationServiceProvider` — resolves named services by name: `GetService(name)` (throws when unregistered) and `GetServiceOrNull(name)` (returns `null`), plus `RegisteredNames` (`IReadOnlySet<string>`) listing the registered named instances (the default is excluded) so an externally supplied name can be validated before resolving. Backed by the container's keyed `IPushNotificationService` registrations; the concrete implementation lives in `Headless.PushNotifications.Core`.
- `PushNotificationResponse` — single-device outcome with three states (`Success`, `Failure`, `Unregistered`); factory methods `Succeeded`, `Failed`, `Unregistered`; query methods `IsSucceeded()`, `IsFailed()`, `IsUnregistered()`; properties `Token`, `MessageId?`, `FailureError?`, `Status`
- `PushNotificationPriority` enum — `High` and `Normal`, the provider-neutral delivery priority of `PushNotificationRequest.Priority`
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
- Input validation: the notification or data-only rule, title ≤ 100 characters and body ≤ 4 000 characters on a notification, `CollapseKey` ≤ 64 UTF-8 bytes
- `CollapseKey` is sent as the Android collapse key and as the `apns-collapse-id` header of the APNs bridge
- `Badge`, `Sound`, `Priority`, and `TimeToLive` map to both the Android config and the APNs bridge, as [Shared delivery fields](#shared-delivery-fields) shows. A data-only request sends no notification block and sets `content-available: 1` with `apns-priority: 5` on the APNs bridge
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

When `Priority` is `null`, Android messages are sent at high priority and the APNs bridge sends no `apns-priority` header. When `Badge` is `null`, no badge is sent on either platform.

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

Apple Push Notification service (APNs) implementation of `IPushNotificationService`, plus the APNs-native `IApnsPushNotificationService`, for iOS push notifications without a Firebase project.

### API and behavior

- One APNs-backed service per instance implements both `IPushNotificationService` and `IApnsPushNotificationService` over HTTP/2. Both interfaces resolve to the same singleton, so they share its HTTP client, provider token, and options: unkeyed for the default instance, keyed by name for a named instance (`[FromKeyedServices("ios")] IApnsPushNotificationService`). `IPushNotificationServiceProvider` returns only `IPushNotificationService`, so resolve a named typed service through keyed DI.
- Authenticates with a `.p8` signing key (token mode) or a `.p12` provider certificate (certificate mode); see [Certificate authentication](#certificate-authentication)
- Selectable as the default (`setup.UseApns(…)`) or as a named instance (`setup.AddNamed("name", i => i.UseApns(…))`). Each slot has four overloads: `IConfiguration`, `Action<ApnsOptions>`, `Action<ApnsOptions, IServiceProvider>`, and a pre-built `ApnsOptions`. Every overload also takes an optional `configureClient` (`Action<HttpClient>`, run after the environment's `BaseAddress` is set, so it may replace it) and an optional `configureResilience` (`Action<HttpStandardResilienceOptions>`)
- Single-device and multicast delivery on both interfaces; a multicast sends one request per token with at most `MaxConcurrency` in flight and returns results in input order
- The shared path turns a notification request into an alert push, `{"aps":{"alert":{"title":…,"body":…},"badge":…,"sound":…}, …}`, and a data-only request into a background push (a VoIP push on a VoIP instance). Each `Data` entry is written as a top-level string field beside `aps`
- `CollapseKey` is sent as the `apns-collapse-id` header, and `TimeToLive` as `apns-expiration`
- A success carries the `apns-id` header value as `MessageId`: a client-generated UUID, or the typed notification's `ApnsId` when set. A resend after an expired provider token keeps the same id
- Production and sandbox environments
- Options validated at startup via FluentValidation and `ValidateOnStart`

Input validation throws `ArgumentException` before any network call when:

- the device token is null, empty, or whitespace, or any token in a multicast is (a multicast checks every token before the first send)
- the multicast token list is null or empty
- the shared request breaks the notification or data-only rule, or a field is out of range
- `Data` contains the reserved key `aps`
- a typed multicast notification sets `ApnsId`
- an `ApnsRawNotification` payload is not a JSON object, or its `Priority` is not allowed for its `Type`
- `CollapseKey` or `CollapseId` is blank or exceeds 64 UTF-8 bytes
- the written JSON payload exceeds 4096 bytes (5120 bytes for a VoIP push)
- a typed notification breaks a rule of its type (see [Typed APNs API](#typed-apns-api))
- the instance cannot send the push type: a VoIP instance sends only alert notifications, and a certificate-mode instance refuses `location`, `fileprovider`, `liveactivity`, `widgets`, and `controls`

### Typed APNs API

`IApnsPushNotificationService.SendAsync(deviceToken, notification, ct)` returns an `ApnsSendResult`; `SendMulticastAsync(deviceTokens, notification, ct)` returns an `ApnsBatchSendResult`. The notification's type decides the push type, topic, priority rules, and allowed fields, so an alert on a background push cannot be expressed. `ApnsNotification` is the closed base record; only this package defines subtypes.

Every notification accepts three optional per-message fields:

- `Expiration` (`ApnsExpiration?`): `ApnsExpiration.At(instant)` sends that instant as `apns-expiration`, and `ApnsExpiration.DeliverOnce` sends `0` (one attempt, no storage). `null` sends no header, so APNs applies its own storage policy, except on VoIP and push-to-talk pushes, where `null` sends `DeliverOnce` because Apple tells senders to use `0` for both. Set `Expiration` to override.
- `CollapseId` (`string?`): sent as `apns-collapse-id`, at most 64 UTF-8 bytes.
- `ApnsId` (`Guid?`): sent as `apns-id` and returned as `ApnsSendResult.ApnsId`, so you can record the id before the send and find the notification in Apple's logs. `null` sends a new random UUID. The resend after an expired provider token keeps the id. Only `SendAsync` accepts it; `SendMulticastAsync` throws `ArgumentException` before any request, because each request needs its own id.

| Type | `apns-push-type` | Topic | Payload | `apns-priority` |
|---|---|---|---|---|
| `ApnsAlertNotification` | `alert`, or `voip` on a VoIP instance | `BundleId`, or `<BundleId>.voip` | `aps` with the alert fields, plus `Data` | `Priority`, else `ApnsOptions.Priority`; 10, 5, or 1 |
| `ApnsBackgroundNotification` | `background` | `BundleId` | `{"aps":{"content-available":1}}` plus `Data` | Always 5 |
| `ApnsLiveActivityNotification` | `liveactivity` | `<BundleId>.push-type.liveactivity` | `aps` with the Live Activity fields | `Priority`, default 5; 10 allowed; 1 refused |
| `ApnsLocationNotification` | `location` | `<BundleId>.location-query` | `{"aps":{}}` plus `Data` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsPushToTalkNotification` | `pushtotalk` | `<BundleId>.voip-ptt` | `{"aps":{}}` plus `Data` | Always 10 |
| `ApnsWidgetsNotification` | `widgets` | `<BundleId>.push-type.widgets` | `{"aps":{"content-changed":true}}` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsControlsNotification` | `controls` | `<BundleId>.push-type.controls` | `{"aps":{"content-changed":true}}` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsComplicationNotification` | `complication` | `<BundleId>.complication` | `{"aps":{}}` plus `Data` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsFileProviderNotification` | `fileprovider` | `<BundleId>.pushkit.fileprovider` | `{"container-identifier":…,"domain":…}`, no `aps` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsRawNotification` | Decided by `Type` | The topic of that push type | `Payload`, written verbatim | The rules of that push type; a fixed priority (background 5, push-to-talk 10) refuses any other |

Only `ApnsAlertNotification` and a raw `Alert` or `Voip` can go through a VoIP instance; the others throw `ArgumentException` there, and a raw `Voip` throws on any other instance. Only the VoIP push type gets the 5120-byte limit; every other type is limited to 4096 bytes.

#### Alert notifications

`ApnsAlertNotification` needs at least one of `Alert`, `Badge`, or `Sound`. Use `ApnsBackgroundNotification` for a silent push.

- `Alert` (`ApnsAlert`): `Title`, `Subtitle`, `Body`, `LaunchImage`, and the localization keys `TitleLocKey`, `SubtitleLocKey`, and `LocKey` with their arguments `TitleLocArgs`, `SubtitleLocArgs`, and `LocArgs`. Each text is a literal or a localization key, not both, and arguments need their key. An alert needs a title, a subtitle, or a body.
- `Badge` (not negative; `0` clears it), `Sound`, `ThreadId`, `Category`, `MutableContent`, `TargetContentId`, and `Data`.
- `Sound` (`ApnsSound`): `ApnsSound.Default`, `ApnsSound.Named(name)`, or `ApnsSound.Critical(name, volume)` with a volume from 0 to 1. `Critical` throws `ArgumentException` for a volume outside that range.
- `InterruptionLevel` (`ApnsInterruptionLevel`): `Passive`, `Active`, `TimeSensitive`, or `Critical`.
- `RelevanceScore`: from 0 to 1.
- `Priority` (`ApnsPriority?`): overrides `ApnsOptions.Priority`. `PowerPrioritized` (1) is allowed here.

A critical sound and the `Critical` interruption level need Apple's critical-alerts entitlement (`com.apple.developer.usernotifications.critical-alerts`). The provider cannot check it, and without it the device plays a critical sound as an ordinary one.

```csharp
var result = await apns.SendAsync(
    deviceToken,
    new ApnsAlertNotification
    {
        Alert = new ApnsAlert { Title = "Order shipped", Subtitle = "#1234", Body = "Arriving tomorrow." },
        Badge = 1,
        Sound = ApnsSound.Default,
        ThreadId = "order-1234",
        InterruptionLevel = ApnsInterruptionLevel.TimeSensitive,
        Data = new JsonObject { ["orderId"] = 1234, ["items"] = new JsonArray("book", "pen") },
        Expiration = ApnsExpiration.At(DateTimeOffset.UtcNow.AddHours(1)),
    },
    ct
);
```

#### Custom data

`Data` on the alert, background, location, push-to-talk, and complication notifications is a `JsonObject` (`System.Text.Json.Nodes`). Apple allows a dictionary, array, string, number, or Boolean for a custom key, so a value can be any JSON. Each key is written beside `aps` at the top of the payload; the key `aps` throws. The provider serializes the object once per send and never modifies it: a multicast writes one payload and reuses it for every token, and the same `JsonObject` can go into later notifications. Do not modify it while a send is preparing. Widgets, controls, Live Activity, and File Provider notifications have fixed payloads and no `Data`. `JsonObject` has no value equality, so two notifications with the same data compare unequal.

#### Raw notifications

`ApnsRawNotification` sends a payload you build yourself, for Apple keys the typed notifications do not model. It takes a required `Type` (`ApnsNotificationType`), a required `Payload` (`JsonElement`, which must be a JSON object), an optional `Priority`, and the common `Expiration`, `CollapseId`, and `ApnsId`.

- `Type` decides everything the typed notification of that push type decides: the `apns-push-type` header, the topic, the default and allowed priorities, the 4096-byte or 5120-byte size limit, the VoIP and push-to-talk deliver-once default, whether a VoIP instance can send it, and whether certificate mode refuses it.
- The payload is sent byte for byte as the element holds it, and its size is measured on those bytes. The provider does not check its keys, so a custom key placed inside `aps` reaches APNs, which ignores it. Prefer a typed notification when one fits.

```csharp
using var payload = JsonDocument.Parse("""{"aps":{"alert":{"title":"Hi"},"new-apple-key":1},"orderId":1234}""");

await apns.SendAsync(
    deviceToken,
    new ApnsRawNotification { Type = ApnsNotificationType.Alert, Payload = payload.RootElement.Clone() },
    ct
);
```

#### Background notifications

`ApnsBackgroundNotification` carries only `Data`, `Expiration`, and `CollapseId`. It has no `Priority`: Apple requires priority 5 for background pushes. The system throttles background pushes and may drop them.

#### Live Activity notifications

`ApnsLiveActivityNotification` starts, updates, or ends a Live Activity.

| `Event` | Device token | Required | Allowed only here |
|---|---|---|---|
| `ApnsLiveActivityEvent.Start` | The app's push-to-start token | `ContentState`, `AttributesType`, `Attributes`, `Alert` | `AttributesType`, `Attributes`, `RequestPushToken` |
| `ApnsLiveActivityEvent.Update` | The running activity's push token | `ContentState` | — |
| `ApnsLiveActivityEvent.End` | The running activity's push token | Nothing; send the final `ContentState` so the ended activity shows the latest data | — |

- `Timestamp` defaults to the clock's current time. The device ignores an update older than the one it shows.
- `RequestPushToken` writes `"input-push-token": 1` inside `aps` on a `Start`, so the started activity (iOS 18 and iPadOS 18 or later) reports a push token for its updates. Setting it on `Update` or `End` throws.
- `StaleDate`, `DismissalDate`, and `RelevanceScore` are optional. A Live Activity relevance score must be a finite number; it ranks the activity against the app's other activities.
- `Alert` shows only a title and a body. Setting `Subtitle`, `SubtitleLocKey`, `SubtitleLocArgs`, or `LaunchImage` throws. Localized texts are written as `{"loc-key":…,"loc-args":[…]}` dictionaries.
- `Sound` needs `Alert` and is written inside it as `aps.alert.sound`. It takes `ApnsSound.Default` or a named sound; a critical sound throws.
- `Priority` defaults to `PowerConsiderate` (5), while Apple defaults to 10, so an update can be delayed. Apple budgets `Immediate` (10) Live Activity pushes per hour, so reserve 10 for updates the user must see now. `PowerPrioritized` (1) throws.
- A VoIP instance and a certificate-mode instance both refuse this push type.

`ContentState` and `Attributes` are `JsonElement` values that must be JSON objects, or the send throws. The provider copies them into the payload as is and never serializes your types, so it stays AOT-safe. Serialize them with your own `JsonSerializerContext`. ActivityKit decodes them into the app's Swift types with default decoding strategies, so the JSON keys must match the Swift property names exactly and no custom date strategy may apply. `JsonElement` has no value equality, so two Live Activity notifications with the same content compare unequal.

```csharp
public sealed record DeliveryAttributes(string OrderId);

public sealed record DeliveryState(string Status, int EtaMinutes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DeliveryAttributes))]
[JsonSerializable(typeof(DeliveryState))]
internal sealed partial class DeliveryJsonContext : JsonSerializerContext;

public sealed class DeliveryActivityPusher(IApnsPushNotificationService apns)
{
    public ValueTask<ApnsSendResult> StartAsync(string pushToStartToken, string orderId, CancellationToken ct)
    {
        return apns.SendAsync(
            pushToStartToken,
            new ApnsLiveActivityNotification
            {
                Event = ApnsLiveActivityEvent.Start,
                AttributesType = "DeliveryAttributes",
                Attributes = JsonSerializer.SerializeToElement(
                    new DeliveryAttributes(orderId),
                    DeliveryJsonContext.Default.DeliveryAttributes
                ),
                ContentState = JsonSerializer.SerializeToElement(
                    new DeliveryState("preparing", 30),
                    DeliveryJsonContext.Default.DeliveryState
                ),
                Alert = new ApnsAlert { Title = "Order confirmed", Body = "Arriving in 30 minutes." },
            },
            ct
        );
    }

    public ValueTask<ApnsSendResult> UpdateAsync(string activityToken, DeliveryState state, CancellationToken ct)
    {
        return apns.SendAsync(
            activityToken,
            new ApnsLiveActivityNotification
            {
                Event = ApnsLiveActivityEvent.Update,
                ContentState = JsonSerializer.SerializeToElement(state, DeliveryJsonContext.Default.DeliveryState),
                StaleDate = DateTimeOffset.UtcNow.AddMinutes(15),
            },
            ct
        );
    }
}
```

#### Other push types

- `ApnsLocationNotification` asks the app's Location Push Service Extension for the device's location. Apple documents no payload, so the provider sends an empty `aps` plus `Data`.
- `ApnsPushToTalkNotification` notifies the app's PushToTalk channel. Send it to the token the PushToTalk framework reports, not a PushKit VoIP token. It always goes at priority 10, and a `null` `Expiration` sends `ApnsExpiration.DeliverOnce`, because a stale push-to-talk push is worse than none. Set `Expiration` to override.
- `ApnsWidgetsNotification` tells WidgetKit to reload the app's widgets. WidgetKit also serves the watch complications that replace ClockKit, so it reloads those too.
- `ApnsControlsNotification` tells the system to reload the app's controls.
- `ApnsComplicationNotification` updates a ClockKit complication. ClockKit is superseded by WidgetKit; prefer `ApnsWidgetsNotification` for widget-based complications. Apple's reference prints this topic's suffix as `h.complication`, which reads as a typo; the provider sends `.complication`, so a `TopicDisallowed` rejection points at that discrepancy.
- `ApnsFileProviderNotification` signals a File Provider domain to sync. `ContainerIdentifier` and `Domain` are required and must not be blank.

#### Results

`ApnsSendResult` holds the provider-agnostic `Response` (`PushNotificationResponse`, the same value the shared interface returns) plus what APNs sent back:

| Property | Value |
|---|---|
| `StatusCode` | The HTTP status, or `null` when no answer arrived (transport fault, timeout, open circuit, refused endpoint) |
| `Reason` | The APNs error code from the body, such as `BadDeviceToken`; `null` on success or when the body has none |
| `ApnsId` | The `apns-id` the request carried (the notification's `ApnsId` when set), which identifies the notification in Apple's logs; `null` when no answer arrived |
| `UniqueId` | The `apns-unique-id` header, which only the sandbox returns, for looking the notification up in Apple's delivery log |
| `InvalidSince` | For HTTP 410, the instant APNs confirmed the token was no longer valid (the body's millisecond `timestamp`); otherwise `null` |

A token the app registered after `InvalidSince` is still valid, so compare the two before deleting a token. `ApnsBatchSendResult` holds `SuccessCount`, `FailureCount` (failed plus unregistered), and `Results`, one per token in input order. The typed API keeps every guarantee of the shared one: per-token outcomes never throw, and only invalid input and caller cancellation do.

### Certificate authentication

An instance uses certificate mode when `Certificate` is set: the base64 text of the APNs provider certificate exported as a PKCS#12 (`.p12`) file with its private key. `CertificatePassword` is optional and opens the file.

- Set token-mode fields (`KeyId`, `TeamId`, `PrivateKey`) or certificate-mode fields (`Certificate`, `CertificatePassword`), not both. Neither, both, or a `CertificatePassword` without a `Certificate` fails `ValidateOnStart`.
- Startup validation also fails when the certificate is not base64 PKCS#12, the password does not open it, it has no private key, or it has expired.
- At host start a hosted service checks the certificate again with `TimeProvider`: an expired certificate fails the start with `InvalidOperationException`, and one that expires within 30 days logs a warning. It then checks the current certificate once a day: within 30 days of expiry it logs a warning, and once the certificate has expired it logs an error without stopping the host. Apple certificates last one year and are renewed by hand.
- The certificate is presented during the TLS handshake. Requests carry no `authorization` header and still carry `apns-topic`, which a certificate valid for several topics requires.
- Certificate mode refuses `location`, `fileprovider`, `liveactivity`, `widgets`, and `controls` pushes with `ArgumentException` before any network call, whether typed or sent as an `ApnsRawNotification` of that `Type`. Apple documents or instructs token authentication for the first four. Apple does not state certificate support for `controls`, so the provider refuses it to be safe. Alert, background, VoIP, push-to-talk, and complication pushes work.
- A renewed certificate takes effect without a restart when the options come from a configuration source that reloads, such as a reloading file or secret provider. When `Certificate` or `CertificatePassword` changes, the instance loads the new certificate, and each new TLS connection presents it. Existing connections keep the previous certificate until they recycle, within 6 hours. A change to any other option does not reload the certificate. Options set in code, or bound from a source that never reloads, still need a restart.
- A renewed certificate that fails validation (not PKCS#12, wrong password, no private key, or expired) is rejected as a whole options change: the reload raises `OptionsValidationException` and every send through the instance throws it until the configuration is corrected. The instance never presents the rejected certificate. Validate the renewed `.p12` before publishing it.
- Key storage depends on the OS. On Linux the private key stays in memory. On Windows it reaches the user key store, because SChannel cannot use an in-memory key for TLS client authentication. On macOS it goes into a temporary keychain.

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseApns(options =>
    {
        options.Certificate = builder.Configuration["Apns:Certificate"]; // base64 .p12, from a secret store
        options.CertificatePassword = builder.Configuration["Apns:CertificatePassword"];
        options.BundleId = "com.example.app";
    })
);
```

### Design constraints

- **Provider tokens are shared per key (token mode).** The provider signs one ES256 provider token per `(team id, key id)` for the whole container and refreshes it every 50 minutes. The default and every named instance that use the same key share that token, because Apple rejects token updates for one key more often than once every 20 minutes (`TooManyProviderTokenUpdates`). Two option sets with the same team id and key id but different `PrivateKey` text are refused: every send through the later one returns `Failure`. Separate processes cannot share the token, so many processes signing with one key can still hit `TooManyProviderTokenUpdates`; prefer a separate key per environment or deployment.
- **Key rotation needs a restart.** The imported key and its token stay cached for the life of the container. A changed `PrivateKey` in reloaded configuration is refused as a different key for the same identity.
- **HTTP/2 only.** Requests require HTTP/2 exactly (`HttpVersionPolicy.RequestVersionExact`). One long-lived connection pool serves each instance (6-hour connection lifetime, hourly keep-alive ping), because Apple asks senders to keep connections open instead of reconnecting per notification.
- **Device tokens stay out of logs.** The `HttpClientFactory` request loggers are removed, because the request URI carries the raw device token. The provider's own log messages mask the token to its first 8 characters.
- **HTTPS only, except loopback.** A non-HTTPS endpoint is refused unless its host is loopback, so a `configureClient` override cannot send the payload or the bearer token in cleartext over a network. The refusal surfaces as a `Failure` on each send.
- **Environment must match the token.** A device token belongs to the environment the app was built for. `Sandbox` is for builds signed with a development profile; `Production` (the default) is for App Store, TestFlight, and ad hoc builds.
- **Push type is per instance for VoIP.** `ApnsOptions.PushType` stays an instance setting: a VoIP instance holds PushKit tokens and sends every shared request and every `ApnsAlertNotification` as a VoIP push, deliver-once unless the request sets an expiration. Register a separate named instance for VoIP.
- **Not supported.** iOS 18 broadcast push channels for Live Activities.

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
        Badge = 1,
        Sound = "default",
        TimeToLive = TimeSpan.FromHours(1),
    },
    ct
);

if (response.IsUnregistered())
    await tokenStore.RemoveAsync(deviceToken, ct);
```

For APNs-only push types, inject `IApnsPushNotificationService` (keyed by name for a named instance) and see [Typed APNs API](#typed-apns-api):

```csharp
public sealed class IosPusher([FromKeyedServices("ios")] IApnsPushNotificationService apns)
{
    public ValueTask<ApnsSendResult> RefreshAsync(string deviceToken, CancellationToken ct) =>
        apns.SendAsync(
            deviceToken,
            new ApnsBackgroundNotification { Data = new Dictionary<string, string> { ["sync"] = "orders" } },
            ct
        );
}
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

For certificate mode, set `Certificate` and, if the `.p12` has one, `CertificatePassword` instead of `KeyId`, `TeamId`, and `PrivateKey`. Supply both from a secret store.

#### ApnsOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `KeyId` | `string?` | _(token mode)_ | 10-character key identifier (ASCII letters and digits) of the APNs signing key. |
| `TeamId` | `string?` | _(token mode)_ | 10-character Apple Developer team identifier that owns the key. |
| `PrivateKey` | `string?` | _(token mode)_ | PEM text of the `AuthKey_*.p8` file, a P-256 EC private key. `ToString()` redacts it and `[JsonIgnore]` excludes it from serialization. |
| `Certificate` | `string?` | _(certificate mode)_ | Base64 text of the provider certificate exported as a `.p12` file with its private key. Setting it selects certificate mode. `ToString()` redacts it and `[JsonIgnore]` excludes it from serialization. |
| `CertificatePassword` | `string?` | `null` | Password that opens `Certificate`, if it has one. Redacted and `[JsonIgnore]` like `Certificate`. |
| `BundleId` | `string` | _(required)_ | App bundle identifier, sent as the `apns-topic` header. Other push types append their suffix, such as `<BundleId>.voip` or `<BundleId>.push-type.liveactivity`. |
| `Environment` | `ApnsEnvironment` | `Production` | `Production` (`api.push.apple.com`) or `Sandbox` (`api.sandbox.push.apple.com`). |
| `PushType` | `ApnsPushType` | `Alert` | `Alert` or `Voip`. `Voip` changes the topic to `<BundleId>.voip` and raises the payload limit to 5120 bytes. |
| `Priority` | `ApnsPriority` | `Immediate` | `Immediate` (10), `PowerConsiderate` (5), or `PowerPrioritized` (1), sent as `apns-priority` on alert and VoIP pushes that set no per-message priority. Other push types use their own rules (see [Typed APNs API](#typed-apns-api)). |
| `TreatBadDeviceTokenAsUnregistered` | `bool` | `false` | Report HTTP 400 `BadDeviceToken` as `Unregistered` instead of `Failure`. Leave off unless the environment is known to be right. |
| `MaxConcurrency` | `int` | `100` | Maximum requests in flight during one multicast. Range 1–1000. |
| `UseAlternativePort` | `bool` | `false` | Deliver through port 2197 instead of 443, in both environments. |
| `Proxy` | `IWebProxy?` | `null` | Proxy applied to the primary handler, so every pooled connection uses it. `[JsonIgnore]`: set from code, not configuration. |
| `MaxConnections` | `int` | `4` | Maximum simultaneous TCP connections one instance opens. Range 1–1000. See [Connections and the connection bound](#connections-and-the-connection-bound). |

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
| HTTP 410 (any reason) | `Unregistered`; the typed API also sets `InvalidSince` from the body's `timestamp` |
| HTTP 400 `BadDeviceToken` | `Failure`, or `Unregistered` when `TreatBadDeviceTokenAsUnregistered` is `true` |
| HTTP 403 `ExpiredProviderToken` | Token mode: one token re-mint and one retry of the send with the new token. The token is never re-minted within 20 minutes of the last mint; in that window the send is not repeated and the rejection is a `Failure`. A second rejection is a `Failure`. Certificate mode never retries |
| Any other rejection, including `DeviceTokenNotForTopic` | `Failure` with `FailureError` `"<reason> (HTTP <status>)"`, or `"APNs rejected the request (HTTP <status>)"` when the body has no reason |
| HTTP 5xx | `Failure`, without an in-process retry. Apple asks senders to wait about 15 minutes before retrying it |
| Transport fault after retries, open circuit breaker, rate-limiter rejection, timeout, refused endpoint | `Failure` with `FailureError` `"<ExceptionType>: <message>"` |
| Caller cancellation | Throws `OperationCanceledException` |

#### Failure classification

The typed API classifies every failure. `ApnsSendResult.FailureKind` is an `ApnsFailureKind` — `DeviceTokenInvalid`, `Throttled`, `ServerError`, `Authentication`, `Configuration`, `Payload`, or `Transport` — derived from the status and the `reason`, following the retry guidance in Apple's "Handling notification responses from APNs":

- **`DeviceTokenInvalid`** — `BadDeviceToken`, `ExpiredToken`, `Unregistered`, and any HTTP 410. Apple lists these among the codes never to retry: remove or correct the token.
- **`Throttled`** — HTTP 429 `TooManyRequests`, which throttles one device token. Retryable with a delay.
- **`ServerError`** — HTTP 5xx. Retryable after 15 minutes (`RetryAfter` carries `TimeSpan.FromMinutes(15)`), per Apple: "After 15 minutes, you can retry JSON payloads that receive response status codes that begin with 5XX."
- **`Authentication`** — the provider token was rejected (`ExpiredProviderToken`, `InvalidProviderToken`, `MissingProviderToken`, `UnrelatedKeyIdInToken`, `BadEnvironmentKeyIdInToken`, `TooManyProviderTokenUpdates`). Only `ExpiredProviderToken` is retryable, and the provider already renewed and retried once; the rest need a key or token fix.
- **`Configuration`** — the instance's APNs setup is wrong: `BadTopic`, `MissingTopic`, `TopicDisallowed`, `DeviceTokenNotForTopic` (the token belongs to another app than the instance's bundle id), `BadCertificate`, `BadCertificateEnvironment`, `Forbidden`.
- **`Payload`** — the request is malformed or too large: `PayloadTooLarge`, `PayloadEmpty`, and the `Bad*`/`Missing*` header errors. Fixing the request is a new request, not a retry, so these are not retryable.
- **`Transport`** — no answer arrived: a transport fault left after the provider's connection retries, an open circuit breaker, a timeout, or APNs' `IdleTimeout`. Retryable, with a duplicate risk the result's `IsRetryable` remarks explain: the fault may have happened after APNs accepted the notification, and APNs does not deduplicate.

`IsRetryable` is `true` only for `ServerError`, `Throttled`, `Transport`, and a result whose reason is still `ExpiredProviderToken`. `RetryAfter` is `TimeSpan?`: 15 minutes for a 5xx, the `Retry-After` header's seconds for a 429 that carries one (Apple's response-header table does not list that header, so it is `null` without one), and `null` otherwise.

An unknown reason — one Apple added later — maps by its status class: 5xx to `ServerError`, 410 to `DeviceTokenInvalid`, 429 to `Throttled`, 403 to `Authentication`, and any other 4xx to `Payload`. The shared `PushNotificationResponse` is unchanged; only the typed `ApnsSendResult` carries the classification.

Two rejection groups log their own error events instead of the per-token warning: `InvalidProviderToken`, `MissingProviderToken`, and `UnrelatedKeyIdInToken` log a configuration error naming the reason, and `TooManyProviderTokenUpdates` logs one naming Apple's 20-minute rule.

#### Connections and the connection bound

Each instance owns one HTTP/2 connection pool and bounds how many TCP connections it opens with `ApnsOptions.MaxConnections` (default 4, range 1-1000). The runtime's own `MaxConnectionsPerServer` cannot provide the bound — it is enforced only for HTTP/1.1 — so the provider enforces it with a connect-callback permit: a dial waits for a free permit, and the connection's stream releases the permit when it is disposed, so a request waits for a free stream on an open connection or for a permit, never queueing blind dials.

The default of 4 follows the connection sizing advice of the mature APNs clients: pushy recommends one or two connections per thread, not exceeding two per APNs server. A cold burst without the bound opened between 7 and 30 connections against a one-stream test server, because APNs allows one stream on a new token-authenticated connection until it sees a valid provider token. Raise `MaxConnections` when you saturate CPU or bandwidth before connection capacity; lower it to shrink the process's footprint.

`ApnsOptions.UseAlternativePort` (default `false`) delivers through port 2197 instead of 443, in both environments, for networks that block 443 to non-web endpoints. `ApnsOptions.Proxy` (default `null`, `[JsonIgnore]`, set from code) applies an `IWebProxy` — such as a corporate egress proxy — to the primary handler, so every connection the pool opens uses it. A proxied HTTP/2 connection tunnels through the proxy with CONNECT, and the proxy's own dial also takes a permit, so leave `MaxConnections` above 1 when a proxy is set.

#### Device tokens from client apps

APNs accepts only the device's APNs token, and each token belongs to one environment. Store what the provider needs with every token:

- **The token kind.** `firebase_messaging`'s `getToken()` returns an FCM registration token, which only Firebase accepts. `getAPNSToken()` returns the raw APNs token on iOS and macOS, and `null` elsewhere or before APNs registration. Never send an FCM token to APNs, and never send an APNs token to Firebase.
- **The environment.** The app's `aps-environment` entitlement decides it: a development build signed from Xcode gets sandbox tokens, and TestFlight and App Store builds get production tokens. `firebase_messaging` mirrors this when it hands the token to Firebase, registering it as sandbox in `DEBUG` builds and as production otherwise. A token sent to the other environment's instance fails with `BadDeviceToken`. That is why `BadDeviceToken` does not mean unregistered by default.
- **One casing.** `firebase_messaging` formats the APNs token as uppercase hex (`%02.2hhX`), while `flutter_apns` uses lowercase (`%02.2hhx`). Normalize to lowercase before storing and deduplicating, so one device is not stored twice.

#### Metrics and tracing

The provider emits OpenTelemetry-compatible metrics and traces through a meter and an activity source both named `Headless.PushNotifications.Apns`. Subscribe with `AddMeter("Headless.PushNotifications.Apns")` / `AddSource("Headless.PushNotifications.Apns")`, or `builder.Services.AddMetrics()` and a `MeterListener` in tests. The device token and payload are never a tag or a span attribute: the token is a stable device identifier.

| Instrument | Kind | Tags |
|---|---|---|
| `headless.apns.sends` | Counter (`{send}`) | `headless.apns.outcome` (`succeeded` / `unregistered` / `failed`), `headless.apns.push_type`, `headless.apns.environment` (`production` / `sandbox`), and on a failure `headless.apns.failure_kind` (the `ApnsFailureKind` in lower snake case) and `headless.apns.reason` (the APNs reason, or `none` when no answer arrived) |
| `headless.apns.send.duration` | Histogram (`ms`) | `headless.apns.push_type`, `headless.apns.environment` |
| `headless.apns.provider_tokens.minted` | Counter (`{token}`) | none |
| `headless.apns.certificates.reloaded` | Counter (`{reload}`) | `headless.apns.outcome` (`accepted` / `rejected`) |

Every device send starts one `apns.send` activity (`ActivityKind.Client`) tagged with `headless.apns.push_type`, `headless.apns.environment`, `headless.apns.outcome`, and on a failure `headless.apns.failure_kind` and `headless.apns.reason`. The activity's status is `Ok` on success and `Error` with the status code and reason as the description on a failure.

#### Retry policy

The provider registers the standard `Microsoft.Extensions.Http.Resilience` handler on its HTTP client (named `Headless:Apns`, or `Headless:Apns:{name}` for a named instance), with these changes:

- Retries only an `HttpRequestException` whose `HttpRequestError` is `ConnectionError`, `NameResolutionError`, or `SecureConnectionError` (the request never reached APNs), at most 2 times. A connection lost after the request was sent is not retried: APNs does not deduplicate, so a resend could deliver the notification twice.
- Never retries an HTTP 5xx. It becomes a `Failure`, because Apple asks senders to wait about 15 minutes before retrying one; retry it from your own queue or job.
- Never retries HTTP 429 `TooManyRequests`, which throttles one device token.
- The circuit breaker counts the retried connection failures, HTTP 500, HTTP 503, and attempt timeouts, not 429, so throttled tokens cannot open the breaker for the whole instance.
- The concurrency limiter queues up to 10 000 requests, so concurrent multicasts wait for a slot instead of being rejected.

Pass `configureResilience` to change any of these.

#### Registrations

- Registers `IPushNotificationService` and `IApnsPushNotificationService` as singletons for the default, or keyed singletons under the instance name for a named instance. Both resolve to the same service instance
- Registers one container-wide provider-token cache, used only by token-mode instances, and `TimeProvider.System` as a singleton if not already registered
- Registers a hosted service per instance that checks a certificate-mode certificate's expiry at host start and daily after that; it does nothing in token mode
- Registration has no network side effects. In token mode the signing key is loaded into the token cache and the first provider token minted on the first send; in certificate mode the certificate is loaded when the HTTP client or the startup check first needs it
