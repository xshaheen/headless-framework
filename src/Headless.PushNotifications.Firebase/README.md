# Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation for push notifications.

## Problem Solved

Provides push notification delivery via Firebase Cloud Messaging using the `IPushNotificationService` abstraction for production mobile app notifications.

## Key Features

- FCM-backed `IPushNotificationService` implementation
- Single device and multicast support
- Custom data payload support
- Automatic token validation
- Detailed error logging
- **Automatic retry** for transient failures (rate limits, temporary outages, server errors)
- Exponential backoff with jitter
- Retry-After header support for rate limits
- Configurable retry policy

## Installation

```bash
dotnet add package Headless.PushNotifications.Firebase
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Bind and validate the "Firebase" configuration section (recommended).
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(builder.Configuration.GetSection("Firebase")));

// Or supply options directly:
builder.Services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(new FirebaseOptions
    {
        Json = await File.ReadAllTextAsync("service-account.json"),
    }));
```

## Configuration

### Basic Setup

```csharp
services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(new FirebaseOptions
    {
        Json = await File.ReadAllTextAsync("firebase-credentials.json"),
    }));
```

### Retry Configuration

```csharp
services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(new FirebaseOptions
    {
        Json = await File.ReadAllTextAsync("firebase-credentials.json"),
        Retry = new FirebaseRetryOptions
        {
            MaxAttempts = 5,                              // 0-10, default: 5
            MaxDelay = TimeSpan.FromMinutes(1),           // default: 1 min
            RateLimitDelay = TimeSpan.FromSeconds(60),    // default: 60s
            UseJitter = true                              // default: true
        }
    }));
```

### Disable Retry

```csharp
services.AddHeadlessPushNotifications(setup =>
    setup.UseFirebase(new FirebaseOptions
    {
        Json = json,
        Retry = new FirebaseRetryOptions { MaxAttempts = 0 } // Disable
    }));
```

### appsettings.json

```json
{
  "Firebase": {
    "Json": "{ ... firebase service account json ... }",
    "Retry": {
      "MaxAttempts": 5,
      "MaxDelay": "00:01:00",
      "RateLimitDelay": "00:01:00",
      "UseJitter": true
    }
  }
}
```

## Retry Behavior

### Transient Errors (Retried)
- `QuotaExceeded` (HTTP 429): Rate limit - uses Retry-After header or RateLimitDelay (default 60s)
- `Unavailable` (HTTP 503): Service temporarily down - exponential backoff
- `Internal` (HTTP 500): Server error - exponential backoff
- `HttpRequestException`: Network issues - exponential backoff
- `TaskCanceledException`: Timeout (not user cancellation) - exponential backoff

### Permanent Errors (No Retry)
- `Unregistered`: Invalid device token - caller should remove token
- `InvalidArgument`: Malformed request - code bug
- `SenderIdMismatch`: Wrong credentials - config error
- `ThirdPartyAuthError`: Bad APNs cert - config error
- User-initiated cancellation via `CancellationToken`

### Backoff Strategy
- Initial delay: 1s
- Exponential backoff: 1s → 2s → 4s → 8s → 16s → 32s...
- Capped at `MaxDelay` (default 60s)
- Jitter (±25%) to prevent thundering herd

### Observability
- Structured logging via `ILogger` for retry attempts
- OpenTelemetry Activity events for distributed tracing
- Polly telemetry auto-emitted via `System.Diagnostics.DiagnosticSource`

## Dependencies

- `Headless.PushNotifications.Abstractions`
- `FirebaseAdmin`
- `Microsoft.Extensions.Http.Resilience`
- `Polly.Core`

## Side Effects

- Registers `IPushNotificationService` as singleton
- Registers a `ResiliencePipeline` named "Headless:FcmRetry"
- Initializes the Firebase Admin SDK lazily on the first send; registration itself has no side effects
