---
domain: Push Notifications
packages: PushNotifications.Abstractions, PushNotifications.Dev, PushNotifications.Firebase
---

# Push Notifications

## Table of Contents
- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Headless.PushNotifications.Abstractions](#headlesspushnotificationsabstractions)
  - [Problem Solved](#problem-solved)
  - [Key Features](#key-features)
  - [Installation](#installation)
  - [Usage](#usage)
  - [Configuration](#configuration)
  - [Dependencies](#dependencies)
  - [Side Effects](#side-effects)
- [Headless.PushNotifications.Dev](#headlesspushnotificationsdev)
  - [Problem Solved](#problem-solved-1)
  - [Key Features](#key-features-1)
  - [Installation](#installation-1)
  - [Quick Start](#quick-start)
  - [Configuration](#configuration-1)
  - [Dependencies](#dependencies-1)
  - [Side Effects](#side-effects-1)
- [Headless.PushNotifications.Firebase](#headlesspushnotificationsfirebase)
  - [Problem Solved](#problem-solved-2)
  - [Key Features](#key-features-2)
  - [Installation](#installation-2)
  - [Quick Start](#quick-start-1)
  - [Configuration](#configuration-2)
    - [Basic Setup](#basic-setup)
    - [Retry Configuration](#retry-configuration)
    - [Disable Retry](#disable-retry)
    - [appsettings.json](#appsettingsjson)
  - [Retry Behavior](#retry-behavior)
    - [Transient Errors (Retried)](#transient-errors-retried)
    - [Permanent Errors (No Retry)](#permanent-errors-no-retry)
    - [Backoff Strategy](#backoff-strategy)
    - [Observability](#observability)
  - [Dependencies](#dependencies-2)
  - [Side Effects](#side-effects-2)

> Provider-agnostic push notification API with Firebase Cloud Messaging for production and a no-op implementation for development.

## Quick Orientation
- Install `Headless.PushNotifications.Abstractions` to depend on the interface only (e.g., in domain/application layers).
- Install `Headless.PushNotifications.Firebase` for production FCM delivery. Register with `AddFirebasePushNotificationService(options => ...)`.
- Install `Headless.PushNotifications.Dev` for local development. Register with `AddNoopPushNotificationService()` — sends nothing, always returns success.
- Use `IPushNotificationService.SendToDeviceAsync(token, title, body, data)` for single-device delivery and `SendMulticastAsync(tokens, title, body)` for batch.
- Firebase supports automatic retry with exponential backoff for transient failures (rate limits, 503s, 500s). Configure via `RetryOptions`.

## Agent Instructions
- Always code against `IPushNotificationService` from Abstractions. Never reference Firebase-specific types in application code.
- Use `Headless.PushNotifications.Dev` in development/testing environments to avoid sending real notifications. Switch using `builder.Environment.IsDevelopment()`.
- Use `Headless.PushNotifications.Firebase` for production FCM. Requires a Firebase service account JSON (via `CredentialsPath` or `GOOGLE_APPLICATION_CREDENTIALS` env var).
- Check `PushNotificationResponse.IsSuccess` after every send. On failure, inspect `.Error` for details.
- Firebase retries transient errors (429, 503, 500) automatically. Permanent errors like `Unregistered` mean the device token is invalid — remove it from your store.
- Both providers register `IPushNotificationService` as **singleton**.
- Do NOT disable retry in production unless you have your own retry infrastructure. Default is 5 attempts with exponential backoff + jitter.

---
# Headless.PushNotifications.Abstractions

Defines the unified interface for push notification services.

## Problem Solved

Provides a provider-agnostic push notification API, enabling seamless switching between push notification providers (Firebase, development) without changing application code.

## Key Features

- `IPushNotificationService` - Core interface for sending notifications
- `PushNotificationResponse` - Single notification response
- `BatchPushNotificationResponse` - Multicast response with success/failure counts
- Support for custom data payloads

## Installation

```bash
dotnet add package Headless.PushNotifications.Abstractions
```

## Usage

```csharp
public sealed class NotificationService(IPushNotificationService pushService)
{
    public async Task SendAsync(string deviceToken, string title, string message)
    {
        var response = await pushService.SendToDeviceAsync(
            deviceToken,
            title,
            message,
            new Dictionary<string, string> { ["orderId"] = "123" }
        );

        if (!response.IsSuccess)
            _logger.LogError("Push failed: {Error}", response.Error);
    }

    public async Task SendToManyAsync(IReadOnlyList<string> tokens, string title, string message)
    {
        var response = await pushService.SendMulticastAsync(tokens, title, message);
        _logger.LogInformation("Sent: {Success}/{Total}", response.SuccessCount, tokens.Count);
    }
}
```

## Configuration

No configuration required. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None.
---
# Headless.PushNotifications.Dev

Development push notification implementation that does nothing.

## Problem Solved

Provides a no-op push notification implementation for development/testing environments, preventing actual notifications from being sent during local development.

## Key Features

- `NoopPushNotificationService` - Silent implementation
- No network calls
- Always returns success responses

## Installation

```bash
dotnet add package Headless.PushNotifications.Dev
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddNoopPushNotificationService();
}
```

## Configuration

No configuration required.

## Dependencies

- `Headless.PushNotifications.Abstractions`

## Side Effects

- Registers `IPushNotificationService` as singleton
---
# Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation for push notifications.

## Problem Solved

Provides push notification delivery via Firebase Cloud Messaging using the `IPushNotificationService` abstraction for production mobile app notifications.

## Key Features

- `GoogleCloudMessagingPushNotificationService` - FCM implementation
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

builder.Services.AddFirebasePushNotificationService(options =>
{
    options.CredentialsPath = "path/to/service-account.json";
    // Or use environment variable: GOOGLE_APPLICATION_CREDENTIALS
});
```

## Configuration

### Basic Setup

```csharp
services.AddPushNotifications(new FirebaseOptions
{
    Json = await File.ReadAllTextAsync("firebase-credentials.json")
});
```

### Retry Configuration

```csharp
services.AddPushNotifications(new FirebaseOptions
{
    Json = await File.ReadAllTextAsync("firebase-credentials.json"),
    Retry = new RetryOptions
    {
        MaxAttempts = 5,                              // 0-10, default: 5
        MaxDelay = TimeSpan.FromMinutes(1),           // default: 1 min
        RateLimitDelay = TimeSpan.FromSeconds(60),    // default: 60s
        UseJitter = true                              // default: true
    }
});
```

### Disable Retry

```csharp
services.AddPushNotifications(new FirebaseOptions
{
    Json = json,
    Retry = new RetryOptions { MaxAttempts = 0 } // Disable
});
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
- Exponential backoff: 1s -> 2s -> 4s -> 8s -> 16s -> 32s...
- Capped at `MaxDelay` (default 60s)
- Jitter (+/-25%) to prevent thundering herd

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
- Registers `ResiliencePipeline` named "Headless:FcmRetry"
- Initializes Firebase Admin SDK
