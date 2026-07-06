# Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation of `IPushNotificationService` for production push notifications.

## Problem Solved

Delivers push notifications to Android (FCM), iOS (via FCM-to-APNs bridge), and Web clients using the FCM v1 API. Handles multicast batching, transient-error retry with exponential backoff, and per-token outcome mapping behind the `IPushNotificationService` interface.

## Key Features

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

## Design Notes

The Firebase Admin SDK `FirebaseApp` is created **lazily on the first send**, not at DI registration time. This means:
- Registration has no observable side effects (no credentials are loaded, no HTTP calls are made).
- Multiple hosts (default plus named instances) in the same process coexist with different credentials — each registration generates a uniquely-named `FirebaseApp`.
- Configuration errors in `FirebaseOptions.Json` (malformed JSON, wrong credential type) surface as exceptions on the first call, not at startup. Supply the `IConfiguration` overload so the options validator catches missing `Json` at startup instead.

Each named instance reads its own options snapshot (`IOptionsMonitor<FirebaseOptions>.Get(name)`) and its own retry pipeline (keyed `Headless:FcmRetry:{name}`); the default reads the unnamed options and the `Headless:FcmRetry` pipeline. Keyed DI does not cascade the key to constructor dependencies, so a keyed sender never reads `CurrentValue` (which binds the default) — the sender is registered through an explicit factory that passes its own name.

Android messages are sent with `Priority.High`; iOS messages include an APNs badge count of 1. These are hardcoded defaults — the `data` payload provides the only customization surface exposed by this abstraction.

## Installation

```bash
dotnet add package Headless.PushNotifications.Firebase
```

## Quick Start

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

## Configuration

### appsettings.json

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

### FirebaseOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `Json` | `string` | _(required)_ | Firebase service account JSON string. Do not log — `ToString()` redacts it. |
| `Retry` | `FirebaseRetryOptions` | see below | Retry policy. |

### FirebaseRetryOptions

| Property | Type | Default | Range | Description |
|---|---|---|---|---|
| `MaxAttempts` | `int` | `5` | 0–10 | Max retry attempts. `0` disables retry. |
| `MaxDelay` | `TimeSpan` | `00:01:00` | 1s–5min | Cap on any single retry delay. |
| `RateLimitDelay` | `TimeSpan` | `00:01:00` | 1s–5min | Delay for HTTP 429 when no Retry-After header is present. |
| `UseJitter` | `bool` | `true` | — | Adds ±25% variance to prevent thundering herd. |

#### Retry overrides

```csharp
// Supply options with a delegate:
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(options =>
    {
        options.Json = configuration["Firebase:Json"]!;
        options.Retry = new FirebaseRetryOptions { MaxAttempts = 3 };
    })
);

// Disable retry:
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(options =>
    {
        options.Json = json;
        options.Retry = new FirebaseRetryOptions { MaxAttempts = 0 };
    })
);
```

### Transient Errors (Retried)

| Error | HTTP | Retry delay |
|---|---|---|
| `QuotaExceeded` | 429 | Retry-After header, or `RateLimitDelay` (default 60s), capped at `MaxDelay` |
| `Unavailable` | 503 | Exponential backoff |
| `Internal` | 500 | Exponential backoff |
| `HttpRequestException` | — | Exponential backoff |
| `TaskCanceledException` (timeout only) | — | Exponential backoff |

### Permanent Errors (No Retry)

| Error | Meaning | Caller action |
|---|---|---|
| `Unregistered` | Token invalid | Returns `PushNotificationResponseStatus.Unregistered`; remove token |
| `InvalidArgument` | Malformed request | Code bug; fix the payload |
| `SenderIdMismatch` | Wrong credentials | Configuration error |
| `ThirdPartyAuthError` | Bad APNs certificate | Configuration error |
| User `CancellationToken` | Caller cancelled | Do not retry |

### Backoff Strategy

- Initial delay: 1s
- Exponential sequence: 1s → 2s → 4s → 8s → 16s → 32s, capped at `MaxDelay` (default 60s)
- Jitter: ±25% (when `UseJitter = true`)
- Retry pipeline key: `"Headless:FcmRetry"` for the default, `"Headless:FcmRetry:{name}"` per named instance (registered via Polly's `AddResiliencePipeline`)

## Dependencies

- `Headless.PushNotifications.Core`
- `Headless.Hosting`
- `FirebaseAdmin`
- `Microsoft.Extensions.Http.Resilience`
- `Polly.Core`

## Side Effects

- Registers `IPushNotificationService` as singleton (`FcmPushNotificationService`) for the default, or a keyed singleton under the instance name for a named instance
- Registers a `ResiliencePipeline` named `"Headless:FcmRetry"` (default) or `"Headless:FcmRetry:{name}"` (per named instance) via Polly
- Registers `TimeProvider.System` as singleton (if not already registered)
- The Firebase Admin SDK `FirebaseApp` is created lazily on first send; registration has no network side effects
