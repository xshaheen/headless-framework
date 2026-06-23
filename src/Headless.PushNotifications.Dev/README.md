# Headless.PushNotifications.Dev

No-op push notification provider for local development and testing.

## Problem Solved

Prevents real notifications from being sent during development or test runs. Uses the same `IPushNotificationService` interface as production so no application code changes are needed when switching environments.

## Key Features

- Silent `IPushNotificationService` implementation (`NoopPushNotificationService`)
- No network calls or external dependencies
- Always returns `Success` responses with a generated GUID as the message id
- Never validates input or throws (inert for any caller, including invalid tokens or empty titles)

## Installation

```bash
dotnet add package Headless.PushNotifications.Dev
```

## Quick Start

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

## Configuration

None. No options or configuration keys.

## Dependencies

- `Headless.PushNotifications.Abstractions`

## Side Effects

- Registers `IPushNotificationService` as singleton (`NoopPushNotificationService`)
