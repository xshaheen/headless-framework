---
domain: Push Notifications
packages: PushNotifications.Abstractions, PushNotifications, PushNotifications.Dev, PushNotifications.Firebase, PushNotifications.Apns
---

# Push Notifications

> One `IPushNotificationService` over Firebase Cloud Messaging and Apple Push Notification service (APNs), plus a no-op provider for development, with an optional default service and any number of named services.

## Orientation

Install one provider package. Each brings `Headless.PushNotifications` (registration) and `Headless.PushNotifications.Abstractions` (contracts). Register in one `AddHeadlessPushNotifications(...)` call: at most one default provider with `setup.Use…()`, plus any number of named services with `setup.AddNamed(name, i => i.Use…())`.

| Provider | Package | Call | Use for |
| --- | --- | --- | --- |
| Firebase Cloud Messaging | `Headless.PushNotifications.Firebase` | `UseFirebase` | Android, Web, and iOS through Firebase's APNs bridge |
| APNs, direct | `Headless.PushNotifications.Apns` | `UseApns` | iOS without a Firebase project, and APNs-only pushes: VoIP, Live Activities, widgets, and more |
| No-op | `Headless.PushNotifications.Dev` | `UseNoop` | Development, tests, and CI |

```csharp
// Firebase as the default, APNs direct for iOS as a named service:
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase"));
    setup.AddNamed("ios", i => i.UseApns(builder.Configuration.GetSection("Apns")));
});
```

Application code sends through `IPushNotificationService`: `SendToDeviceAsync(clientIdentifier, request, ct)` for one device token and `SendMulticastAsync(clientIdentifiers, request, ct)` for many. Two provider interfaces cover what the shared request cannot express:

- `IFcmPushNotificationService` (Firebase): topic and condition sends, Android, web push, and raw APNs-bridge options, analytics labels, dry runs, and the FCM error code and retry guidance of each result.
- `IApnsPushNotificationService` (APNs): Live Activities, rich alerts, background, location, push-to-talk, widgets, controls, complications, File Provider, broadcast channels, and the APNs status, reason, and ids of each result.

Each resolves wherever its provider's `IPushNotificationService` resolves.

**Verify:** invalid provider options fail the host start with `OptionsValidationException`. To prove Firebase credentials without delivering anything, send an `FcmMessage` with `DryRun = true` through `IFcmPushNotificationService` and assert `result.Response.IsSucceeded()`.

## Agent Rules

- Call `AddHeadlessPushNotifications` once per service collection, with at most one default `Use…`. A second call, or two defaults, throws `InvalidOperationException`. The default is optional: with only named services, plain `IPushNotificationService` is not registered.
- Resolve a named service with `[FromKeyedServices("name")] IPushNotificationService`, or `IPushNotificationServiceProvider.GetService("name")`. The provider never returns the default service.
- Check `PushNotificationResponse.Status` after every send. An unregistered token returns `false` from both `IsSucceeded()` and `IsFailed()`, so call `IsUnregistered()` and delete that token from your store.
- Depend on `IPushNotificationService`, not a provider type. Take `IFcmPushNotificationService` or `IApnsPushNotificationService` only for what the shared request cannot express, and keep plain alerts and data-only messages on the shared interface so the provider stays swappable.
- Never call the Firebase Admin SDK (`FirebaseAdmin`, `FirebaseMessaging`) directly. Route every send through these interfaces.
- Use `UseNoop()` in development and tests, switched on `builder.Environment.IsDevelopment()`, so no real notification goes out.
- Never rely on a data-only message arriving. It is a background push, and iOS throttles and may drop background pushes.
- Load `FirebaseOptions.Json`, `ApnsOptions.PrivateKey`, `Certificate`, and `CertificatePassword` from a secret store or an environment variable, never from committed configuration. `ToString()` redacts them and they are excluded from JSON serialization, but do not log them.
- Retry a failure from `IsRetryable` and `RetryAfter` on `FcmSendResult` or `ApnsSendResult`, not from the error text. A Firebase failure that needs a later retry, and every APNs 5xx, belong in your own queue or job.
- Never retry an APNs HTTP 429 `TooManyRequests` at once. It throttles that one device token.
- Point each APNs instance at the environment its tokens came from. `BadDeviceToken` stays a `Failure`, not `Unregistered`, unless you set `TreatBadDeviceTokenAsUnregistered`, because a wrong `Environment` would otherwise discard every valid token.
- Restart the process to rotate an APNs signing key. A renewed APNs certificate needs no restart when its configuration source reloads.

## Core Concepts

### Default and named clients

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Push:Primary"));                         // default, optional
    setup.AddNamed("driver-app", i => i.UseFirebase(builder.Configuration.GetSection("Push:Driver"))); // keyed "driver-app"
    setup.AddNamed("test-sink", i => i.UseNoop());                                                // keyed "test-sink"
});
```

- **Names.** Each name must be non-whitespace and unique (ordinal) within the call, and each named service must choose exactly one provider. A violation throws `ArgumentException` or `InvalidOperationException`, and the service collection is left unchanged.
- **Resolution.** A named service resolves as keyed `IPushNotificationService`, or through `IPushNotificationServiceProvider`: `GetService(name)` throws `InvalidOperationException` when the name is not registered, and `GetServiceOrNull(name)` returns `null`. Both throw `ArgumentException` for a null or whitespace name. To check a name that came from outside, test `RegisteredNames` (`IReadOnlySet<string>`, named services only) instead of probing.
- **Provider interfaces.** `IPushNotificationServiceProvider` returns only `IPushNotificationService`. Resolve a named typed service through keyed DI: `[FromKeyedServices("ios")] IApnsPushNotificationService`.
- **Isolation.** One provider can back several names. Each named service has its own options, validated per name at startup, and never reads the default's configuration. A Firebase service has its own retry settings and its own Firebase app. An APNs service has its own HTTP client and resilience pipeline (`Headless:Apns:{name}`) and shares only the provider-token cache, keyed by team id and key id.

### Notification and data-only requests

A `PushNotificationRequest` is one of two kinds:

| Kind | `Title` and `Body` | `Data` | `Badge`, `Sound` | What the device does |
| --- | --- | --- | --- | --- |
| Notification | Both non-blank | Optional | Optional | Shows the notification |
| Data-only | Both `null` | At least one entry | Not allowed | Wakes the app in the background and shows nothing |

Setting only one of `Title` and `Body` makes the request a notification, so the other must be non-blank too. Firebase and APNs throw `ArgumentException` before any network call for any other combination, a negative `Badge` or `TimeToLive`, a `TimeToLive` over 28 days (`ArgumentOutOfRangeException`), a blank `Sound`, or an undefined `Priority`. `UseNoop()` validates nothing.

```csharp
// Silent sync signal: no title, no body, data only.
await pushService.SendToDeviceAsync(
    deviceToken,
    new PushNotificationRequest { Data = new Dictionary<string, string> { ["sync"] = "orders" } },
    ct
);
```

### Shared delivery fields

Every field is an optional `init` property, and `null` means "not set".

| Field | APNs provider | Firebase: Android | Firebase: APNs bridge |
| --- | --- | --- | --- |
| `Badge` (`int?`, not negative) | `aps.badge`; `0` clears the badge | Notification count; `0` is not sent, because Android cannot clear a badge | `aps.badge` |
| `Sound` (`string?`, a bundled sound name or `default`) | `aps.sound` | Notification sound | `aps.sound` |
| `Priority` (`PushNotificationPriority?`: `High`, `Normal`) | `High` sends `apns-priority: 10`, `Normal` sends `5`. `null` uses `ApnsOptions.Priority` | `High` or `Normal`. `null` sends high | `High` sends `10`, `Normal` sends `5`. `null` sends no header |
| `TimeToLive` (`TimeSpan?`, 0 to 28 days) | `apns-expiration` = now + TTL in epoch seconds. `TimeSpan.Zero` sends `0`: one attempt, no storage | Message TTL | `apns-expiration`, computed the same way |
| `CollapseKey` (`string?`, at most 64 UTF-8 bytes) | `apns-collapse-id` | Collapse key | `apns-collapse-id` |

- `CollapseKey` makes a newer notification replace an older undelivered one with the same key. Both production providers throw `ArgumentException` for a key over 64 UTF-8 bytes, Apple's header limit.
- A `null` `TimeToLive` sends no expiration, so the provider's storage policy applies. An APNs VoIP instance is the exception: it sends `apns-expiration: 0`, because Apple wants VoIP pushes delivered at once or not at all.
- A `null` `Badge` sends no badge on either provider.

A data-only request maps as follows:

- **APNs.** On an alert instance: a background push, `{"aps":{"content-available":1}}` plus `Data`, push type `background`, always priority 5, as Apple requires. On a VoIP instance: a VoIP push, `{"aps":{}}` plus `Data`, at the request's `Priority` or `ApnsOptions.Priority`.
- **Firebase.** No notification block. Android priority follows `Priority` (high when `null`). The APNs bridge gets `aps.content-available: 1` and `apns-priority: 5`, whatever `Priority` says.

### Response Status Model

Every send returns a `PushNotificationResponse` (`ClientIdentifier`, `Status`, `MessageId`, `FailureError`) in one of three states:

| `Status` | `IsSucceeded()` | `IsFailed()` | `IsUnregistered()` | Meaning |
| --- | --- | --- | --- | --- |
| `Success` | `true` | `false` | `false` | Accepted by the provider. `MessageId` is set. |
| `Failure` | `false` | `true` | `false` | Rejected or not delivered. `FailureError` is set. |
| `Unregistered` | `false` | `false` | `true` | The token is no longer valid. Delete it. |

Firebase reports `Unregistered` for an FCM `Unregistered` error. APNs reports it for HTTP 410, and for `BadDeviceToken` when you opt in. `if (!response.IsSucceeded())` treats an unregistered token as a failure. In a `BatchPushNotificationResponse`, `FailureCount` counts every response that did not succeed, unregistered tokens included.

### Multicast and Batching

`SendMulticastAsync` sends one request to many tokens and returns a `BatchPushNotificationResponse` (`SuccessCount`, `FailureCount`, `Responses`). `Responses` has one entry per input token, in input order. A transport failure that remains after retries becomes a `Failure` for the tokens it hit; results already collected are kept. Only invalid input and caller cancellation throw.

- **Firebase** sends any number of tokens in batches of at most 500, the FCM limit. Batches run one after another, and each batch's messages go out at once, so one multicast can have up to 500 requests in flight. Split the token list yourself to bound concurrency more tightly. A batch that fails as a whole after all retries is a `Failure` for every token in it.
- **APNs** has no batch endpoint. It sends one request per token, with at most `ApnsOptions.MaxConcurrency` (default 100) in flight. It validates every token before the first send, so one blank token cannot cause a partial delivery.

## Choosing a Provider

| | Firebase | APNs | No-op |
| --- | --- | --- | --- |
| **Use when** | Production apps on Android, iOS (through the APNs bridge), or Web | Production iOS apps that should not depend on a Firebase project, or need VoIP, Live Activity, or other APNs-only pushes | Local development, tests, CI |
| **Avoid when** | Local development (real credentials, real sends) | Android or Web clients; local development | Any production environment |
| **Credentials** | Firebase service account JSON (`FirebaseOptions.Json`) | A `.p8` signing key with key id and team id, or a `.p12` provider certificate; plus the bundle id | None |
| **Retry** | The SDK retries 503 and network errors; the provider adds `Internal` and `QuotaExceeded` retries with Google's delays | At most 2 retries of connection failures before a request is sent; a 5xx is a `Failure` for you to retry after about 15 minutes | None |
| **Trade-off** | Needs a Firebase project and a service account | iOS only; one request per token; key rotation needs a restart | Always succeeds |

---

## Headless.PushNotifications.Abstractions

The contracts: `IPushNotificationService`, `IPushNotificationServiceProvider`, `PushNotificationRequest`, `PushNotificationResponse`, `BatchPushNotificationResponse`, `PushNotificationPriority`, and `PushNotificationResponseStatus`. Reference it alone from a library that leaves registration to the host. It has no registration and no options.

```csharp
public sealed class NotificationService(
    IPushNotificationService pushService,
    IDeviceTokenStore tokenStore,
    ILogger<NotificationService> logger
)
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
            await tokenStore.RemoveAsync(deviceToken, ct);
        }
        else if (response.IsFailed())
        {
            logger.LogError("Push notification failed: {Error}", response.FailureError);
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

        foreach (var r in result.Responses)
        {
            if (r.IsUnregistered())
            {
                await tokenStore.RemoveAsync(r.ClientIdentifier, ct);
            }
        }
    }
}
```

---

## Headless.PushNotifications

`AddHeadlessPushNotifications(Action<HeadlessPushNotificationsSetupBuilder>)` and the `IPushNotificationServiceProvider` implementation. Every provider package brings it, so you rarely install it directly.

### Setup

```bash
dotnet add package Headless.PushNotifications
```

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseNoop();                                // default, optional
    setup.AddNamed("driver-app", i => i.UseNoop()); // keyed "driver-app"
});

var driver = app.Services.GetRequiredService<IPushNotificationServiceProvider>().GetService("driver-app");
```

**Verify:** `IPushNotificationServiceProvider.RegisteredNames` contains every `AddNamed` name. A misconfigured call (second default, duplicate name, named service with no provider) throws at registration.

### Configuration

None. Each provider binds its own options in its `Use…` call.

### Design and runtime behavior

`AddHeadlessPushNotifications` validates the whole setup before it touches the service collection, so a call that throws leaves it unchanged. It then registers `IPushNotificationServiceProvider`, the default service (unkeyed), and each named service (keyed by name).

---

## Headless.PushNotifications.Dev

A no-op `IPushNotificationService` for development and tests.

### Setup

```bash
dotnet add package Headless.PushNotifications.Dev
```

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHeadlessPushNotifications(setup => setup.UseNoop());
}
else
{
    builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(builder.Configuration.GetSection("Firebase")));
}
```

**Verify:** in a test, `SendToDeviceAsync` returns `IsSucceeded()` with a GUID `MessageId`.

### Design and runtime behavior

- Registers a singleton `IPushNotificationService` (keyed by name for a named service).
- Makes no network call, never validates input, never throws, and always returns `Success` with a new GUID as the message id.
- Takes no options.

---

## Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM v1) through the Firebase Admin SDK, behind `IPushNotificationService` and `IFcmPushNotificationService`.

### Setup

```bash
dotnet add package Headless.PushNotifications.Firebase
```

```csharp
// Bind from configuration, so the options validator runs at startup.
builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(builder.Configuration.GetSection("Firebase")));

// One Firebase project per app:
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseFirebase(builder.Configuration.GetSection("Firebase:Primary"));
    setup.AddNamed("driver-app", i => i.UseFirebase(builder.Configuration.GetSection("Firebase:Driver")));
});
```

`UseFirebase` also takes an `Action<FirebaseOptions>`, an `Action<FirebaseOptions, IServiceProvider>`, or a `FirebaseOptions` instance.

**Verify:** the host fails to start with `OptionsValidationException` when `Json` is missing. The Firebase app is created on the first send, so a malformed or wrong service-account JSON is not caught at startup: it makes every send a `Failure` with `FcmFailureKind.Authentication`. Send an `FcmMessage` with `DryRun = true` after deploy and assert `result.Response.IsSucceeded()`.

### Configuration

```json
{
  "Firebase": {
    "Json": "{ ...service account JSON... }",
    "TreatSenderIdMismatchAsUnregistered": false,
    "Retry": {
      "MaxAttempts": 2,
      "MaxDelay": "00:05:00"
    }
  }
}
```

| `FirebaseOptions` | Default | Description |
| --- | --- | --- |
| `Json` | required | Service account JSON. Secret: `ToString()` redacts it. |
| `Retry` | see below | `FirebaseRetryOptions`. |
| `TreatSenderIdMismatchAsUnregistered` | `false` | Reports `SenderIdMismatch` as `Unregistered`, so you delete the token. Enable it only once the credentials are known to belong to the right Firebase project; with the wrong project, every token gets this error. |

| `FirebaseRetryOptions` | Default | Range | Description |
| --- | --- | --- | --- |
| `MaxAttempts` | `2` | 0–5 | Provider retries after the first attempt. `0` turns them off; the SDK's own retries still run. |
| `MaxDelay` | `00:05:00` | 1 minute to 1 hour | Cap on one retry delay. A failure whose Retry-After is longer is not retried. |

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(options =>
    {
        options.Json = builder.Configuration["Firebase:Json"]!;
        options.Retry = new FirebaseRetryOptions { MaxAttempts = 0 }; // you retry from your own queue
    })
);
```

### Design and runtime behavior

- **Registrations.** `IPushNotificationService` and `IFcmPushNotificationService` resolve to the same singleton (keyed by name for a named service). Also adds `TimeProvider.System` when none is registered. Registration makes no network call.
- **Never throws for a target's failure.** A timeout, a credential error, or any other exception becomes a `Failure`. Only the caller's cancellation throws `OperationCanceledException`, from single, multicast, topic, and condition sends alike.
- **Validation** before any network call (`ArgumentException`): the notification or data-only rule, `CollapseKey` over 64 UTF-8 bytes, and reserved data keys. The reserved keys are `from`, `notification`, `message_type`, and any key starting with `google.` or `gcm.`; a key such as `googleId` is allowed. Title and body have no length limit.
- **Payload size.** FCM limits the whole message to 4,096 bytes and a topic message to 2,048 bytes. The provider does not check. FCM rejects an oversized message with `InvalidArgument`, a `Failure`.
- **Duplicates.** FCM has no idempotency key, so a retried message can arrive twice. Set `Retry.MaxAttempts = 0` when you retry from your own queue.

#### Retried errors

| Error | HTTP | Retried by | Delay |
| --- | --- | --- | --- |
| `Unavailable` | 503 | Firebase Admin SDK, up to 4 times (not configurable) | Retry-After up to 30 s, else 1, 2, 4, 8 s |
| Network error (`HttpRequestException`) | — | Firebase Admin SDK, up to 4 times | 1, 2, 4, 8 s |
| `Internal` | 500 | This provider, up to `MaxAttempts` times | 10 s, 20 s, 40 s, …, each up to 50% longer; a longer Retry-After wins |
| `QuotaExceeded` | 429 | This provider, up to `MaxAttempts` times | The longer of Retry-After and 60 s |

Every provider delay is capped at `MaxDelay`, and a Retry-After longer than `MaxDelay` ends the retries. Delays follow the registered `TimeProvider`. A multicast resends only the tokens that failed with `Internal` or `QuotaExceeded`, waits once for the longest delay among them, and merges the results back in input order.

#### Errors that are not retried

| Error | Meaning | Result |
| --- | --- | --- |
| `Unregistered` | The token is invalid | `Unregistered`; delete the token |
| `SenderIdMismatch` | The token belongs to another Firebase project | `Failure`, or `Unregistered` with `TreatSenderIdMismatchAsUnregistered` |
| `InvalidArgument` | Malformed request, or payload over 4,096 bytes | `Failure`; fix the payload. Never a dead token |
| `ThirdPartyAuthError` | Bad APNs certificate or key in the Firebase project | `Failure`; a configuration error |
| Timeout (`TaskCanceledException` the caller did not request) | The request timed out | `Failure` |
| Credential error | Service-account JSON could not be loaded or exchanged | `Failure` |
| Caller `CancellationToken` | You cancelled | Throws `OperationCanceledException` |

### Typed FCM API

`IFcmPushNotificationService` sends an `FcmMessage`: the FCM v1 message with the options the shared request cannot express.

| Method | Target | Returns |
| --- | --- | --- |
| `SendAsync(fid, message, ct)` | One device's FID or registration token | `FcmSendResult` |
| `SendMulticastAsync(fids, message, ct)` | Many devices, in batches of at most 500 | `FcmBatchSendResult` (`SuccessCount`, `FailureCount`, `Results` in input order) |
| `SendToTopicAsync(topic, message, ct)` | Every device subscribed to the topic | `FcmSendResult` whose client identifier is the topic |
| `SendToConditionAsync(condition, message, ct)` | Every device whose subscriptions satisfy the condition | `FcmSendResult` whose client identifier is the condition |

Every method validates the message and target first and throws `ArgumentException` for invalid input. Pass a topic without the `/topics/` prefix: the prefix, or a character outside `[a-zA-Z0-9-_.~%]`, throws. A condition must name one to five topics. Every send uses the same retry loop and never throws for the target's failure.

```csharp
public sealed class ShippingPusher(IFcmPushNotificationService fcm)
{
    public async Task NotifyAsync(string fid, CancellationToken ct)
    {
        var result = await fcm.SendAsync(
            fid,
            new FcmMessage
            {
                Notification = new FcmNotification { Title = "Order shipped", Body = "Your order is on its way." },
                Data = new Dictionary<string, string> { ["orderId"] = "1234" },
                Android = new FcmAndroidOptions
                {
                    ChannelId = "shipping",
                    Priority = FcmAndroidPriority.High,
                    Visibility = FcmAndroidVisibility.Public,
                },
                Webpush = new FcmWebpushOptions { Link = new Uri("https://example.com/orders/1234") },
                Apns = new FcmApnsOptions
                {
                    Payload = new JsonObject { ["aps"] = new JsonObject { ["interruption-level"] = "time-sensitive" } },
                },
                AnalyticsLabel = "order_shipped",
            },
            ct
        );

        if (result.Response.IsUnregistered())
        {
            // Delete the FID from your store.
        }
        else if (result.IsRetryable)
        {
            // Requeue after result.RetryAfter.
        }
    }
}
```

#### Message fields

| Field | Sent as | Rules |
| --- | --- | --- |
| `Notification` (`Title`, `Body`, `Image`) | `notification` | `Image` must be an absolute URL. `null` sends a data message. |
| `Data` | `data` | The same reserved keys as the shared request. |
| `Android.Priority`, `TimeToLive`, `CollapseKey`, `DirectBootOk` | `android.priority`, `ttl`, `collapse_key`, `direct_boot_ok` | `TimeToLive` 0 to 28 days; `Priority` a defined `FcmAndroidPriority`. |
| `Android.ChannelId`, `Tag`, `Color`, `Icon`, `ClickAction`, `Sound`, `NotificationCount`, `Visibility`, `Image` | `android.notification.*` | Sent only when one is set. `Color` is `#RRGGBB`; `NotificationCount` is 0 or more; `Image` is absolute. |
| `Webpush.Link`, `Headers`, `Data` | `webpush.fcm_options.link`, `headers`, `data` | `Link` must be an absolute HTTPS URL. |
| `Apns.Headers`, `Payload` | `apns.headers`, `apns.payload` | `Payload` is the whole APNs payload as a `JsonObject` and must hold an `aps` object; custom top-level keys pass through. FCM merges `Notification` into `aps.alert`. |
| `AnalyticsLabel` | `fcm_options.analytics_label`, on every message of a multicast | Matches `^[a-zA-Z0-9-_.~%]{1,50}$`. |
| `DryRun` | `validate_only` | FCM validates without delivering. A valid message succeeds with a placeholder message id. |

The shared `IPushNotificationService` builds an `FcmMessage` from each `PushNotificationRequest` and sends it the same way, so both paths produce the same FCM JSON for the same fields.

#### Failure classification

`FcmSendResult` holds the shared `Response` plus `ErrorCode` (the wire code, such as `QUOTA_EXCEEDED`; `null` on success or when no answer arrived), `FailureKind` (`FcmFailureKind`), `IsRetryable`, and `RetryAfter`. The result reflects the last attempt, after the provider's own retries.

| Cause | `ErrorCode` | `FailureKind` | Status | `IsRetryable` | `RetryAfter` |
| --- | --- | --- | --- | --- | --- |
| `UNREGISTERED` | `UNREGISTERED` | `TokenInvalid` | `Unregistered` | No | — |
| `SENDER_ID_MISMATCH` | `SENDER_ID_MISMATCH` | `Configuration`, or `TokenInvalid` with `TreatSenderIdMismatchAsUnregistered` | `Failure`, or `Unregistered` | No | — |
| `INVALID_ARGUMENT` | `INVALID_ARGUMENT` | `Payload`; never a dead token | `Failure` | No | — |
| `THIRD_PARTY_AUTH_ERROR` | `THIRD_PARTY_AUTH_ERROR` | `Authentication` | `Failure` | No | — |
| `QUOTA_EXCEEDED` | `QUOTA_EXCEEDED` | `Throttled` | `Failure` | Yes | Retry-After, else 60 s |
| `INTERNAL`, `UNAVAILABLE` | `INTERNAL`, `UNAVAILABLE` | `ServerError` | `Failure` | Yes | Retry-After, else 10 s |
| No FCM code: 400 | `INVALID_ARGUMENT` | `Payload` | `Failure` | No | — |
| No FCM code: 401 | `UNAUTHENTICATED` | `Authentication` | `Failure` | No | — |
| No FCM code: 403, 404 | `PERMISSION_DENIED`, `NOT_FOUND` | `Configuration`; never a dead token | `Failure` | No | — |
| No FCM code: 429 | `RESOURCE_EXHAUSTED` | `Throttled` | `Failure` | Yes | Retry-After, else 60 s |
| No FCM code: 409 or 5xx | `CONFLICT`, `INTERNAL`, `UNAVAILABLE`, `UNKNOWN` | `ServerError` | `Failure` | Yes | Retry-After, else 10 s |
| Network failure after the SDK's retries, or a timeout | `null` | `Transport` | `Failure` | Yes (may deliver twice) | — |
| Service-account JSON cannot be loaded, or the token exchange is refused | `null` | `Authentication` | `Failure` | No | — |

### Telemetry

Subscribe to `FcmDiagnostics.SourceName` (`Headless.PushNotifications.Firebase`) with `AddMeter` and `AddSource`. The attribute names are the `FcmTags` constants.

| Instrument | Unit | Records | Tags |
| --- | --- | --- | --- |
| `headless.fcm.sends` | `{send}` | One final outcome per target, after retries | `headless.fcm.instance`, `headless.fcm.outcome` (`succeeded`, `unregistered`, `failed`), `headless.fcm.target_kind` (`token`, `topic`, `condition`); on a failure also `headless.fcm.failure_kind` and `headless.fcm.error_code` (`none` without an answer) |
| `headless.fcm.send.duration` | `s` | One send attempt or one multicast round | `headless.fcm.instance`, `headless.fcm.target_kind`, `headless.fcm.operation` (`send`, `multicast`) |
| `headless.fcm.retries` | `{retry}` | One in-process resend of one target | `headless.fcm.instance`, `headless.fcm.target_kind`, `headless.fcm.failure_kind`, `headless.fcm.error_code` |

`headless.fcm.instance` is the service name, or `default`. Traces: one `fcm.send` client activity per device, topic, or condition send, spanning its retries, with the outcome, failure kind, error code, retry count, and dry-run flag; it is an error only for a `Failure`. A multicast emits one `fcm.multicast` activity per round of at most 500 devices, with the batch size, retry round, and success count. Tokens, topic and condition text, and message content are never tags.

---

## Headless.PushNotifications.Apns

APNs over HTTP/2, called directly, behind `IPushNotificationService`, `IApnsPushNotificationService`, and `IApnsBroadcastChannelService`. No Firebase project needed.

### Setup

```bash
dotnet add package Headless.PushNotifications.Apns
```

```csharp
// Bind from configuration so the options validator runs at startup.
// Supply the key from a secret store, such as the Apns__PrivateKey environment variable.
builder.Services.AddHeadlessPushNotifications(setup => setup.UseApns(builder.Configuration.GetSection("Apns")));

// A VoIP service beside the alert service, sharing one key and so one provider token:
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

`UseApns` takes an `IConfiguration` section, an `Action<ApnsOptions>`, an `Action<ApnsOptions, IServiceProvider>`, or an `ApnsOptions` instance. Each also takes an optional `configureClient` (`Action<HttpClient>`, run after the environment's `BaseAddress` is set, so it can replace it) and an optional `configureResilience` (`Action<HttpStandardResilienceOptions>`).

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

Sending through the shared interface:

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
{
    await tokenStore.RemoveAsync(deviceToken, ct);
}
```

**Verify:** the host fails to start with `OptionsValidationException` for missing or mixed credentials, a missing `BundleId`, or, in certificate mode, a certificate that is not valid PKCS#12, has no private key, or has expired. Registration makes no network call, so send to a sandbox device token from a development build and assert `IsSucceeded()`. A token from the wrong environment fails with reason `BadDeviceToken`.

### Configuration

| `ApnsOptions` | Default | Description |
| --- | --- | --- |
| `KeyId` | token mode | 10-character key id (ASCII letters and digits) of the APNs signing key. |
| `TeamId` | token mode | 10-character Apple Developer team id that owns the key. |
| `PrivateKey` | token mode | PEM text of the `AuthKey_*.p8` file, a P-256 EC private key. Secret. |
| `Certificate` | certificate mode | Base64 text of the provider certificate exported as a `.p12` file with its private key. Setting it selects certificate mode. Secret. |
| `CertificatePassword` | `null` | Password of `Certificate`, if it has one. Secret. |
| `BundleId` | required | App bundle id, sent as `apns-topic`. Other push types append their suffix, such as `<BundleId>.voip`. |
| `Environment` | `Production` | `Production` (`api.push.apple.com`) for App Store, TestFlight, and ad hoc builds, or `Sandbox` (`api.sandbox.push.apple.com`) for builds signed with a development profile. |
| `PushType` | `Alert` | `Alert` or `Voip`. `Voip` sets the topic to `<BundleId>.voip` and raises the payload limit to 5,120 bytes. |
| `Priority` | `Immediate` | `Immediate` (10), `PowerConsiderate` (5), or `PowerPrioritized` (1), for alert and VoIP pushes that set no priority. Other push types follow their own rules ([Typed APNs API](#typed-apns-api)). |
| `TreatBadDeviceTokenAsUnregistered` | `false` | Reports HTTP 400 `BadDeviceToken` as `Unregistered`. Leave off unless the environment is known to be right. |
| `MaxConcurrency` | `100` | Requests in flight during one multicast. 1 to 1,000. |
| `MaxConnections` | `4` | TCP connections one service opens. 1 to 1,000. See [Connections and the connection bound](#connections-and-the-connection-bound). |
| `UseAlternativePort` | `false` | Uses port 2197 instead of 443, for networks that block 443 to non-web endpoints. |
| `Proxy` | `null` | `IWebProxy` for every connection. Set it in code; configuration cannot bind it. |

`PrivateKey`, `Certificate`, and `CertificatePassword` are redacted by `ToString()` and excluded from JSON serialization.

Change the resilience pipeline:

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseApns(
        builder.Configuration.GetSection("Apns"),
        configureResilience: options => options.Retry.MaxRetryAttempts = 1
    )
);
```

### Design and runtime behavior

#### Registrations

- `IPushNotificationService` and `IApnsPushNotificationService` resolve to the same singleton, sharing its HTTP client, provider token, and options: unkeyed for the default, keyed by name for a named service.
- `IApnsBroadcastChannelService` is registered the same way, with its own HTTP client (`Headless:Apns:Channels`, or `Headless:Apns:Channels:{name}`) that shares the service's credentials, proxy, resilience rules, and `MaxConnections` bound.
- One provider-token cache for the whole container, and `TimeProvider.System` when none is registered.
- A hosted service per certificate-mode service checks the certificate's expiry at start and daily.
- Registration makes no network call. In token mode the key is loaded and the first provider token minted on the first send. In certificate mode the certificate is loaded when the HTTP client or the startup check first needs it.

#### Validation

The shared and typed paths throw `ArgumentException` before any network call when:

- a device token is null, empty, or whitespace (a multicast checks every token before the first send), or the multicast token list is null or empty;
- the shared request breaks the notification or data-only rule, or a field is out of range;
- `Data` contains the reserved key `aps`. Every other `Data` key is written at the top level of the payload, beside `aps`, never inside it;
- `CollapseKey` or `CollapseId` is blank or over 64 UTF-8 bytes;
- the written JSON payload, `Data` included, is over 4,096 bytes (5,120 for a VoIP push);
- a typed notification breaks a rule of its type, or a multicast sets `ApnsId`;
- the service cannot send the push type: a VoIP service sends only alerts, and a certificate-mode service refuses `location`, `fileprovider`, `liveactivity`, `widgets`, and `controls` pushes.

#### Status mapping

| APNs answer | Result |
| --- | --- |
| HTTP 200 | `Success`. `MessageId` is the `apns-id` the request carried: a new UUID, or the typed notification's `ApnsId`. |
| HTTP 410 (any reason) | `Unregistered`. The typed result also sets `InvalidSince`. |
| HTTP 400 `BadDeviceToken` | `Failure`, or `Unregistered` with `TreatBadDeviceTokenAsUnregistered`. |
| HTTP 403 `ExpiredProviderToken` | Token mode: the token is minted again and the send retried once, with the same `apns-id`. Within 20 minutes of the last mint, the send is not repeated and the rejection is a `Failure`. A second rejection is a `Failure`. Certificate mode never retries. |
| Any other rejection, including `DeviceTokenNotForTopic` | `Failure` with `FailureError` `"<reason> (HTTP <status>)"`, or `"APNs rejected the request (HTTP <status>)"` when the body has no reason. |
| HTTP 5xx | `Failure`, not retried in-process. Apple asks senders to wait about 15 minutes before retrying. |
| Transport fault after retries, open circuit breaker, rate-limiter rejection, timeout, refused endpoint | `Failure` with `FailureError` `"<ExceptionType>: <message>"`. |
| Caller cancellation | Throws `OperationCanceledException`. |

`InvalidProviderToken`, `MissingProviderToken`, and `UnrelatedKeyIdInToken` log a configuration error naming the reason, and `TooManyProviderTokenUpdates` logs an error naming Apple's 20-minute rule, instead of the per-token warning.

#### Failure classification

`ApnsSendResult.FailureKind` (`ApnsFailureKind`) classifies every failure on the typed path:

| `FailureKind` | Causes | `IsRetryable` |
| --- | --- | --- |
| `DeviceTokenInvalid` | `BadDeviceToken`, `ExpiredToken`, `Unregistered`, any HTTP 410. Delete or correct the token. | No |
| `Throttled` | HTTP 429 `TooManyRequests`, which throttles one device token | Yes, with a delay |
| `ServerError` | HTTP 5xx | Yes, after 15 minutes |
| `Authentication` | `ExpiredProviderToken`, `InvalidProviderToken`, `MissingProviderToken`, `UnrelatedKeyIdInToken`, `BadEnvironmentKeyIdInToken`, `TooManyProviderTokenUpdates` | Only `ExpiredProviderToken` (already renewed and retried once); the rest need a key or token fix |
| `Configuration` | `BadTopic`, `MissingTopic`, `TopicDisallowed`, `DeviceTokenNotForTopic` (the token belongs to another app), `BadCertificate`, `BadCertificateEnvironment`, `Forbidden` | No |
| `Payload` | `PayloadTooLarge`, `PayloadEmpty`, and the `Bad*` and `Missing*` header errors. Fixing the request makes a new request. | No |
| `Transport` | No answer: a transport fault after the connection retries, an open circuit breaker, a timeout, or APNs' `IdleTimeout` | Yes, but the notification may already have been accepted, and APNs does not deduplicate |

`RetryAfter` is 15 minutes for a 5xx, the `Retry-After` header's seconds for a 429 that carries one, and `null` otherwise. A reason Apple adds later maps by status: 5xx to `ServerError`, 410 to `DeviceTokenInvalid`, 429 to `Throttled`, 403 to `Authentication`, any other 4xx to `Payload`. Only the typed `ApnsSendResult` carries the classification.

#### Retry policy

The HTTP client's pipeline (`Headless:Apns`, or `Headless:Apns:{name}`) replaces any host-wide resilience handler, such as the one `AddHeadless()` service defaults add, so a stock retry never resends a 429 or 5xx. Starting from the standard `Microsoft.Extensions.Http.Resilience` handler:

- It retries only an `HttpRequestException` whose `HttpRequestError` is `ConnectionError`, `NameResolutionError`, or `SecureConnectionError` (the request never reached APNs), at most 2 times. A connection lost after the request was sent is not retried, because APNs does not deduplicate and a resend could show the notification twice.
- It never retries an HTTP 5xx or a 429.
- The circuit breaker counts the retried connection failures, HTTP 500, HTTP 503, and attempt timeouts, not 429, so throttled tokens cannot open it for the whole service.
- The concurrency limiter queues up to 10,000 requests, so concurrent multicasts wait instead of failing.

Pass `configureResilience` to change any of these.

#### Provider tokens

In token mode, one ES256 provider token per `(team id, key id)` serves the whole container, refreshed every 50 minutes. The default and every named service on the same key share it, because Apple rejects token updates for one key more often than once every 20 minutes with `TooManyProviderTokenUpdates`. Separate processes cannot share it, so many processes on one key can still hit that limit: use a separate key per environment or deployment.

The imported key stays cached for the life of the container, so rotating the key needs a restart. Two option sets with the same team id and key id but different `PrivateKey` text are refused: every send through the later one returns `Failure`.

#### Connections and the connection bound

Each service keeps one long-lived HTTP/2 connection pool (6-hour connection lifetime, hourly keep-alive ping), as Apple asks, and opens at most `MaxConnections` TCP connections (default 4). A request waits for a free stream on an open connection or for a free connection slot. Raise `MaxConnections` when you saturate CPU or bandwidth before connection capacity; lower it to shrink the process's footprint. With a `Proxy`, the proxy's own dial also takes a slot, so keep `MaxConnections` above 1.

A non-HTTPS endpoint is refused unless its host is loopback, so a `configureClient` override cannot send the payload or the bearer token in cleartext. The refusal is a `Failure` on each send.

Device tokens stay out of logs: the request URI holds the raw token, so the HTTP client's request logging is off, and the provider's own log messages show only the token's first 8 characters.

#### Device tokens from client apps

APNs accepts only the device's APNs token, and each token belongs to one environment. Store with every token:

- **Its kind.** In a Flutter app, `firebase_messaging`'s `getToken()` returns an FCM registration token, which only Firebase accepts. `getAPNSToken()` returns the raw APNs token on iOS and macOS, and `null` elsewhere or before APNs registration. Never send an FCM token to APNs, or an APNs token to Firebase.
- **Its environment.** The app's `aps-environment` entitlement decides it: a development build signed from Xcode gets sandbox tokens, and TestFlight and App Store builds get production tokens. `firebase_messaging` registers the token with Firebase as sandbox in `DEBUG` builds and as production otherwise. A token sent to the other environment's service fails with `BadDeviceToken`.
- **One casing.** `firebase_messaging` formats the APNs token as uppercase hex, while `flutter_apns` uses lowercase. Normalize to lowercase before you store and deduplicate, so one device is not stored twice.

#### Metrics and tracing

The meter and activity source are both `ApnsDiagnostics.SourceName` (`Headless.PushNotifications.Apns`). Subscribe with `AddMeter` and `AddSource`, or use `builder.Services.AddMetrics()` and a `MeterListener` in tests. The device token and payload are never tags.

| Instrument | Kind | Tags |
| --- | --- | --- |
| `headless.apns.sends` | Counter (`{send}`) | `headless.apns.outcome` (`succeeded`, `unregistered`, `failed`), `headless.apns.push_type`, `headless.apns.environment` (`production`, `sandbox`); on a failure also `headless.apns.failure_kind` (lower snake case) and `headless.apns.reason` (`none` when no answer arrived) |
| `headless.apns.send.duration` | Histogram (`ms`) | `headless.apns.push_type`, `headless.apns.environment` |
| `headless.apns.provider_tokens.minted` | Counter (`{token}`) | none |
| `headless.apns.certificates.reloaded` | Counter (`{reload}`) | `headless.apns.outcome` (`accepted`, `rejected`) |

Every device send starts one `apns.send` client activity with the push type, environment, outcome, and on a failure the failure kind and reason. Its status is `Ok` on success and `Error`, described by the status code and reason, on a failure.

### Certificate authentication

An APNs service uses certificate mode when `Certificate` is set: the base64 text of the provider certificate exported as a `.p12` file with its private key. `CertificatePassword` is optional.

- Set the token-mode fields (`KeyId`, `TeamId`, `PrivateKey`) or the certificate-mode fields, not both. Neither, both, or a `CertificatePassword` without a `Certificate` fails startup validation.
- Startup validation also fails when the certificate is not base64 PKCS#12, the password does not open it, it has no private key, or it has expired.
- At host start, an expired certificate fails the start with `InvalidOperationException`, and one that expires within 30 days logs a warning. Then, once a day, a certificate within 30 days of expiry logs a warning, and an expired one logs an error without stopping the host. Apple certificates last one year and are renewed by hand.
- Requests carry no `authorization` header and still carry `apns-topic`, which a certificate valid for several topics needs.
- Certificate mode refuses `location`, `fileprovider`, `liveactivity`, `widgets`, and `controls` pushes to a device token with `ArgumentException`, typed or raw. Use token mode for those. Alert, background, VoIP, push-to-talk, and complication pushes work, and channel management and broadcasts work too.
- A renewed certificate takes effect without a restart when the options come from a configuration source that reloads. When `Certificate` or `CertificatePassword` changes, new TLS connections present the new certificate. Existing connections keep the old one until they recycle, within 6 hours. Options set in code, or bound from a source that never reloads, need a restart.
- A renewed certificate that fails validation is rejected as a whole options change: every send through the service throws `OptionsValidationException` until the configuration is fixed, and the service never presents the rejected certificate. Validate the `.p12` before you publish it.
- Key storage depends on the OS: in memory on Linux, the user key store on Windows (SChannel cannot use an in-memory key for TLS client authentication), and a temporary keychain on macOS.

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

**Verify:** a certificate that is not PKCS#12, has no private key, or has expired fails startup with `OptionsValidationException`, and an expired one fails the host start with `InvalidOperationException`. A certificate within 30 days of expiry logs a warning at start. After a reload, `headless.apns.certificates.reloaded` records `accepted` or `rejected`.

### Typed APNs API

`IApnsPushNotificationService.SendAsync(deviceToken, notification, ct)` returns an `ApnsSendResult`, and `SendMulticastAsync(deviceTokens, notification, ct)` returns an `ApnsBatchSendResult`. The notification's type decides the push type, topic, priority rules, and allowed fields. `ApnsNotification` is a closed base record; only this package defines its subtypes.

```csharp
public sealed class IosPusher([FromKeyedServices("ios")] IApnsPushNotificationService apns)
{
    public ValueTask<ApnsSendResult> RefreshAsync(string deviceToken, CancellationToken ct) =>
        apns.SendAsync(deviceToken, new ApnsBackgroundNotification { Data = new JsonObject { ["sync"] = "orders" } }, ct);
}
```

Every notification takes three optional fields:

- `Expiration` (`ApnsExpiration?`): `ApnsExpiration.At(instant)` sends that instant as `apns-expiration`, and `ApnsExpiration.DeliverOnce` sends `0` (one attempt, no storage). `null` sends no header, except on VoIP and push-to-talk pushes, where `null` means `DeliverOnce`.
- `CollapseId` (`string?`): `apns-collapse-id`, at most 64 UTF-8 bytes.
- `ApnsId` (`Guid?`): `apns-id`, returned as `ApnsSendResult.ApnsId`, so you can record it before the send and find the notification in Apple's logs. `null` sends a new UUID. Only `SendAsync` accepts it; `SendMulticastAsync` throws `ArgumentException`.

| Type | `apns-push-type` | Topic | Payload | `apns-priority` |
| --- | --- | --- | --- | --- |
| `ApnsAlertNotification` | `alert`, or `voip` on a VoIP service | `BundleId`, or `<BundleId>.voip` | `aps` with the alert fields, plus `Data` | `Priority`, else `ApnsOptions.Priority`; 10, 5, or 1 |
| `ApnsBackgroundNotification` | `background` | `BundleId` | `{"aps":{"content-available":1}}` plus `Data` | Always 5 |
| `ApnsLiveActivityNotification` | `liveactivity` | `<BundleId>.push-type.liveactivity` | `aps` with the Live Activity fields | `Priority`, default 5; 10 allowed; 1 refused |
| `ApnsLocationNotification` | `location` | `<BundleId>.location-query` | `{"aps":{}}` plus `Data` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsPushToTalkNotification` | `pushtotalk` | `<BundleId>.voip-ptt` | `{"aps":{}}` plus `Data` | Always 10 |
| `ApnsWidgetsNotification` | `widgets` | `<BundleId>.push-type.widgets` | `{"aps":{"content-changed":true}}` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsControlsNotification` | `controls` | `<BundleId>.push-type.controls` | `{"aps":{"content-changed":true}}` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsComplicationNotification` | `complication` | `<BundleId>.complication` | `{"aps":{}}` plus `Data` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsFileProviderNotification` | `fileprovider` | `<BundleId>.pushkit.fileprovider` | `{"container-identifier":…,"domain":…}`, no `aps` | `Priority`, default 10; 5 allowed; 1 refused |
| `ApnsRawNotification` | Set by `Type` | The topic of that push type | `Payload`, verbatim | The rules of that push type |

A VoIP service sends only `ApnsAlertNotification` and an `ApnsRawNotification` of type `Alert` or `Voip`, each as a VoIP push. Every other type throws `ArgumentException` there. A raw `Voip` throws on any other service. Only VoIP pushes get the 5,120-byte limit.

`ApnsSendResult` holds the shared `Response` plus `StatusCode` (`null` when no answer arrived), `Reason` (the APNs error code), `ApnsId` (`null` when no answer arrived), `UniqueId` (the `apns-unique-id` header, sandbox only, for Apple's delivery log), `InvalidSince` (for HTTP 410, when APNs confirmed the token invalid), `FailureKind`, `IsRetryable`, and `RetryAfter`. A token the app registered after `InvalidSince` is still valid, so compare the two before you delete a token. `ApnsBatchSendResult` holds `SuccessCount`, `FailureCount` (failed plus unregistered), and `Results` in input order. Per-token outcomes never throw; only invalid input and caller cancellation do.

#### Alert notifications

`ApnsAlertNotification` needs at least one of `Alert`, `Badge`, or `Sound`. Use `ApnsBackgroundNotification` for a silent push.

- `Alert` (`ApnsAlert`): `Title`, `Subtitle`, `Body`, `LaunchImage`, and the localization keys `TitleLocKey`, `SubtitleLocKey`, and `LocKey` with their arguments `TitleLocArgs`, `SubtitleLocArgs`, and `LocArgs`. Each text is a literal or a localization key, not both, and arguments need their key. An alert needs a title, a subtitle, or a body.
- `Badge` (not negative; `0` clears it), `ThreadId`, `Category`, `MutableContent`, `TargetContentId`, and `Data`.
- `Sound` (`ApnsSound`): `ApnsSound.Default`, `ApnsSound.Named(name)`, or `ApnsSound.Critical(name, volume)` with a volume from 0 to 1 (`ArgumentException` otherwise).
- `InterruptionLevel` (`ApnsInterruptionLevel`): `Passive`, `Active`, `TimeSensitive`, or `Critical`.
- `RelevanceScore`: 0 to 1.
- `Priority` (`ApnsPriority?`): overrides `ApnsOptions.Priority`. `PowerPrioritized` (1) is allowed here.

A critical sound and the `Critical` interruption level need Apple's critical-alerts entitlement (`com.apple.developer.usernotifications.critical-alerts`) on the app. The provider cannot check it; without it the device plays a critical sound as an ordinary one.

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

`Data` on the alert, background, location, push-to-talk, and complication notifications is a `JsonObject` (`System.Text.Json.Nodes`), so a value can be any JSON. The shared `PushNotificationRequest.Data` stays a string map, because FCM data messages carry only strings. Each key is written beside `aps`; the key `aps` throws. The provider serializes the object once per send and never changes it, so a multicast reuses one payload and the same `JsonObject` can go into later notifications. Do not change it while a send is preparing. Widgets, controls, Live Activity, and File Provider notifications have fixed payloads and no `Data`. `JsonObject` has no value equality, so two notifications with the same data compare unequal.

#### Raw notifications

`ApnsRawNotification` sends a payload you build, for Apple keys the typed notifications do not model. It takes a required `Type` (`ApnsNotificationType`), a required `Payload` (`JsonElement`, a JSON object), an optional `Priority`, and the common fields.

- `Type` decides what the typed notification of that push type decides: the header, topic, priorities (a fixed priority, background 5 or push-to-talk 10, refuses any other), size limit, deliver-once default, and whether a VoIP or certificate-mode service can send it.
- The payload is sent byte for byte and measured on those bytes. They must be strict JSON: an element parsed with `AllowTrailingCommas` or comment handling that kept a trailing comma or a comment throws `ArgumentException`. Keys are not checked, so a custom key inside `aps` reaches APNs, which ignores it. Prefer a typed notification when one fits.

```csharp
using var payload = JsonDocument.Parse("""{"aps":{"alert":{"title":"Hi"},"new-apple-key":1},"orderId":1234}""");

await apns.SendAsync(
    deviceToken,
    new ApnsRawNotification { Type = ApnsNotificationType.Alert, Payload = payload.RootElement.Clone() },
    ct
);
```

#### Background notifications

`ApnsBackgroundNotification` carries only `Data`, `Expiration`, and `CollapseId`. It has no `Priority`: Apple requires 5. The system throttles background pushes and may drop them.

#### Live Activity notifications

`ApnsLiveActivityNotification` starts, updates, or ends a Live Activity.

| `Event` | Device token | Required | Allowed only here |
| --- | --- | --- | --- |
| `ApnsLiveActivityEvent.Start` | The app's push-to-start token | `ContentState`, `AttributesType`, `Attributes`, `Alert` | `AttributesType`, `Attributes`, `RequestPushToken`, `InputPushChannel` |
| `ApnsLiveActivityEvent.Update` | The running activity's push token | `ContentState` | — |
| `ApnsLiveActivityEvent.End` | The running activity's push token | Nothing; send the final `ContentState` so the ended activity shows the latest data | — |

- The push-to-start token and the activity's push token are not the device token used for alerts.
- `Timestamp` defaults to the clock's current time. The device ignores an update older than the one it shows.
- `RequestPushToken` writes `"input-push-token": 1` on a `Start`, so the started activity (iOS 18 and iPadOS 18 or later) reports a push token for its updates. Setting it on `Update` or `End` throws.
- `InputPushChannel` writes `"input-push-channel": "<channel id>"` on a `Start`, so the activity (iOS 18 and later) listens for [broadcasts](#broadcast-channels-ios-18) on that channel. Setting it on `Update` or `End`, or to a blank id, throws.
- `StaleDate`, `DismissalDate`, and `RelevanceScore` are optional. A Live Activity relevance score must be finite. It ranks the activity against the app's other activities.
- `Alert` shows only a title and a body. `Subtitle`, `SubtitleLocKey`, `SubtitleLocArgs`, or `LaunchImage` throws. Localized texts are written as `{"loc-key":…,"loc-args":[…]}` objects.
- `Sound` needs `Alert` and is written as `aps.alert.sound`. A critical sound throws.
- `Priority` defaults to `PowerConsiderate` (5), while Apple defaults to 10, so an update can be delayed. Apple budgets `Immediate` (10) Live Activity pushes per hour, so reserve 10 for updates the user must see now. `PowerPrioritized` (1) throws.
- A VoIP service and a certificate-mode service refuse this push type to a device token. A certificate-mode service can still broadcast it.

`ContentState` and `Attributes` are `JsonElement` values that must be JSON objects, or the send throws. They are copied into the payload as is, so serialize them with your own `JsonSerializerContext`; the provider stays AOT-safe. ActivityKit decodes them into the app's Swift types with default decoding, so the JSON keys must match the Swift property names exactly and no custom date strategy may apply. `JsonElement` has no value equality, so two notifications with the same content compare unequal.

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
- `ApnsPushToTalkNotification` notifies the app's PushToTalk channel. Send it to the token the PushToTalk framework reports, not a PushKit VoIP token. It always goes at priority 10, and a `null` `Expiration` means `DeliverOnce`.
- `ApnsWidgetsNotification` tells WidgetKit to reload the app's widgets, including WidgetKit watch complications.
- `ApnsControlsNotification` tells the system to reload the app's controls.
- `ApnsComplicationNotification` updates a ClockKit complication. ClockKit is superseded by WidgetKit, so prefer `ApnsWidgetsNotification`. The topic suffix is `.complication`; if APNs answers `TopicDisallowed`, check that suffix against Apple's current documentation.
- `ApnsFileProviderNotification` signals a File Provider domain to sync. `ContainerIdentifier` and `Domain` are required and must not be blank.

#### Broadcast channels (iOS 18)

A broadcast sends one Live Activity update or end to every device subscribed to a channel, in one request. Devices on iOS 18 and iPadOS 18 or later subscribe by starting the activity with `InputPushChannel` set to the channel id.

`IApnsBroadcastChannelService` manages the app's channels on APNs' channel-management host (`api-manage-broadcast.push.apple.com:2196`, or `api-manage-broadcast.sandbox.push.apple.com:2195` in the sandbox):

| Method | Result |
| --- | --- |
| `CreateAsync(storagePolicy, ct)` | `ApnsBroadcastChannel` with the id APNs generated |
| `GetAsync(channelId, ct)` | The channel's storage policy |
| `ListAsync(ct)` | Every active channel id |
| `DeleteAsync(channelId, ct)` | Nothing. Irreversible; APNs may still deliver messages it stored. |

- `ApnsChannelStoragePolicy.NoMessageStored` delivers each message once and allows a higher publishing budget, for frequent updates such as live scores. `MostRecentMessageStored` keeps the latest message up to 8 hours for offline devices, for infrequent updates such as flight status. The policy is fixed at creation.
- An app holds up to 10,000 channels per environment, and a channel does not cross environments. Delete a channel when its event is over.
- A channel id is a base64 string of no fixed length. Do not store it in a fixed-size column.
- A rejection throws `ApnsRequestException` with the HTTP status, the APNs reason, and the `apns-request-id` to quote to Apple. A transport fault throws the underlying exception.

`IApnsPushNotificationService.SendBroadcastAsync(channelId, notification, ct)` posts the update on the service's regular APNs host:

- It takes an `ApnsLiveActivityNotification` with `Update` or `End`. A `Start`, any of `AttributesType`, `Attributes`, `RequestPushToken`, or `InputPushChannel`, or a `CollapseId` throws `ArgumentException` before any request.
- A `null` `Expiration` sends `0` (deliver once, never store), which every storage policy accepts. APNs rejects a nonzero expiration on a `NoMessageStored` channel.
- `apns-priority` defaults to 5 and accepts 1, 5, and 10. `ApnsId`, when set, becomes the `apns-request-id`. The payload limit is 5,120 bytes.
- It returns an `ApnsBroadcastResult` and never throws for a rejection or a transport fault: `IsSucceeded`, `StatusCode`, `Reason`, `FailureError`, `RequestId`, `UniqueId`, `FailureKind`, `IsRetryable`, and `RetryAfter`, classified like a device send.

```csharp
public sealed class MatchBroadcaster(IApnsBroadcastChannelService channels, IApnsPushNotificationService apns)
{
    public async ValueTask<string> OpenAsync(CancellationToken ct)
    {
        // Frequent score updates: store nothing, publish more.
        var channel = await channels.CreateAsync(ApnsChannelStoragePolicy.NoMessageStored, ct);

        return channel.Id; // share with the apps; each starts its activity with InputPushChannel = channel.Id
    }

    public ValueTask<ApnsBroadcastResult> ScoreAsync(string channelId, JsonElement score, CancellationToken ct)
    {
        return apns.SendBroadcastAsync(
            channelId,
            new ApnsLiveActivityNotification { Event = ApnsLiveActivityEvent.Update, ContentState = score },
            ct
        );
    }

    public ValueTask CloseAsync(string channelId, CancellationToken ct) => channels.DeleteAsync(channelId, ct);
}
```
