---
domain: Push Notifications
packages: PushNotifications.Abstractions, PushNotifications.Dev, PushNotifications.Firebase
---

# Push Notifications

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
    - [Response Status Model](#response-status-model)
    - [Provider Extension Model](#provider-extension-model)
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
- [Headless.PushNotifications.Dev](#headlesspushnotificationsdev)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.PushNotifications.Firebase](#headlesspushnotificationsfirebase)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Design Notes](#design-notes)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Provider-agnostic push notification API with Firebase Cloud Messaging for production and a no-op implementation for development.

## Quick Orientation

Install `Headless.PushNotifications.Abstractions` to depend on the interface only (e.g., in domain/application layers). Register exactly one provider via the builder in the host:

```csharp
// Production — Firebase Cloud Messaging
builder.Services.AddHeadlessPushNotifications(setup => setup.UseFirebase(builder.Configuration.GetSection("Firebase")));

// Development / testing — no-op, always succeeds
builder.Services.AddHeadlessPushNotifications(setup => setup.UseNoop());
```

Both methods live on the `IServiceCollection` extension `AddHeadlessPushNotifications`. Each provider package contributes its `Use{Provider}` extension member on `HeadlessPushNotificationsSetupBuilder`.

Use `IPushNotificationService.SendToDeviceAsync` for single-device delivery and `SendMulticastAsync` for batch sends. Always check `PushNotificationResponse.Status` — three states apply: `Success`, `Failure`, and `Unregistered`.

## Agent Instructions

- Always code against `IPushNotificationService` from `Headless.PushNotifications.Abstractions`. Never reference `FcmPushNotificationService` or other concrete types in application code.
- Use `Headless.PushNotifications.Dev` (`UseNoop()`) in development and testing environments to avoid sending real notifications. Switch on `builder.Environment.IsDevelopment()`.
- Do NOT call the Firebase Admin SDK (`FirebaseAdmin`, `FirebaseMessaging`) directly. Route all sends through `IPushNotificationService`.
- Exactly one provider must be selected. Zero or multiple `Use{Provider}` calls throw `InvalidOperationException` at registration. Do not call `AddHeadlessPushNotifications` twice.
- After every send, check `PushNotificationResponse.Status`. Three distinct states exist — `Success`, `Failure`, and `Unregistered` — and `IsSucceeded()` / `IsFailed()` both return `false` for an unregistered token. Use `IsUnregistered()` explicitly and remove that token from your store.
- FCM enforces content limits: **title ≤ 100 characters**, **body ≤ 4 000 characters**. `FcmPushNotificationService` throws `ArgumentException` if either limit is exceeded.
- FCM data payload keys `from`, `notification`, `message_type`, and any key starting with `google` or `gcm` are reserved. Passing a reserved key throws `ArgumentException` from `SendToDeviceAsync` / `SendMulticastAsync` before any network call.
- Multicast sends are transparently chunked into batches of at most 500 tokens (the FCM hard limit). A single `SendMulticastAsync` call handles any number of tokens.
- Firebase retries transient errors (HTTP 429 `QuotaExceeded`, 503 `Unavailable`, 500 `Internal`, network errors, non-user timeouts) automatically with exponential backoff. Do not wrap calls in your own retry for these errors.
- Permanent errors (`Unregistered`, `InvalidArgument`, `SenderIdMismatch`, `ThirdPartyAuthError`) are not retried. `Unregistered` is returned as `PushNotificationResponseStatus.Unregistered`, not as a failure.
- Do not disable retry in production (`MaxAttempts = 0`) unless you have your own resilience infrastructure. Default is 5 attempts with exponential backoff and jitter.
- `FirebaseOptions.Json` contains sensitive private-key material. Do not log it, serialize it, or store it in configuration as plain text in production. The `ToString()` override on `FirebaseOptions` redacts it.

## Core Concepts

### Response Status Model

Every send returns a `PushNotificationResponse` with one of three mutually exclusive states:

| `Status` | `IsSucceeded()` | `IsFailed()` | `IsUnregistered()` | Meaning |
|---|---|---|---|---|
| `Success` | `true` | `false` | `false` | Accepted by FCM; `MessageId` is non-null |
| `Failure` | `false` | `true` | `false` | Provider rejected the message; `FailureError` is non-null |
| `Unregistered` | `false` | `false` | `true` | Token is no longer valid; remove from your store |

The `Unregistered` state is not a failure in the FCM model — it is a signal to clean up stale tokens. A simple `if (!response.IsSucceeded())` will miss the unregistered case.

### Provider Extension Model

`AddHeadlessPushNotifications` accepts a callback that receives a `HeadlessPushNotificationsSetupBuilder`. Provider packages each contribute a `Use{Provider}` C# 14 extension member on that builder. The framework enforces exactly one provider per registration — this prevents accidental dual-registration and makes the active provider visible in the setup call.

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
- `PushNotificationResponse` — single-device outcome with three states (`Success`, `Failure`, `Unregistered`); factory methods `Succeeded`, `Failed`, `Unregistered`; query methods `IsSucceeded()`, `IsFailed()`, `IsUnregistered()`; properties `Token`, `MessageId?`, `FailureError?`, `Status`
- `PushNotificationResponseStatus` enum — `Success`, `Failure`, `Unregistered`
- `BatchPushNotificationResponse` — multicast aggregate: `SuccessCount`, `FailureCount`, `Responses` (one per token)
- `HeadlessPushNotificationsSetupBuilder` — builder used by `AddHeadlessPushNotifications` to select exactly one provider
- `IPushNotificationsProviderOptionsExtension` — hook implemented by each provider package to register its services; exposed so third-party providers can integrate

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

### Configuration

None. This is an abstractions-only package.

### Dependencies

None.

### Side Effects

None. This package defines only interfaces and contracts.
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

- `Headless.PushNotifications.Abstractions`

### Side Effects

- Registers `IPushNotificationService` as singleton (`NoopPushNotificationService`)
---
## Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation of `IPushNotificationService` for production push notifications.

### Problem Solved

Delivers push notifications to Android (FCM), iOS (via FCM-to-APNs bridge), and Web clients using the FCM v1 API. Handles multicast batching, transient-error retry with exponential backoff, and per-token outcome mapping behind the `IPushNotificationService` interface.

### Key Features

- FCM-backed `IPushNotificationService` implementation (`FcmPushNotificationService`)
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
- Multiple hosts in the same process can coexist with different credentials — each registration generates a uniquely-named `FirebaseApp`.
- Configuration errors in `FirebaseOptions.Json` (malformed JSON, wrong credential type) surface as exceptions on the first call, not at startup. Supply the `IConfiguration` overload so the options validator catches missing `Json` at startup instead.

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
- Retry pipeline key: `"Headless:FcmRetry"` (registered via Polly's `AddResiliencePipeline`)

### Dependencies

- `Headless.PushNotifications.Abstractions`
- `FirebaseAdmin`
- `Microsoft.Extensions.Http.Resilience`
- `Polly.Core`

### Side Effects

- Registers `IPushNotificationService` as singleton (`FcmPushNotificationService`)
- Registers a `ResiliencePipeline` named `"Headless:FcmRetry"` (via Polly)
- Registers `TimeProvider.System` as singleton (if not already registered)
- The Firebase Admin SDK `FirebaseApp` is created lazily on first send; registration has no network side effects
