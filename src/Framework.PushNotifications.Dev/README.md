# Framework.PushNotifications.Dev

Development push notification implementation that does nothing.

## Problem Solved

Provides a no-op push notification implementation for development/testing environments, preventing actual notifications from being sent during local development.

## Key Features

- `NoopPushNotificationService` - Silent implementation
- No network calls
- Always returns success responses

## Installation

```bash
dotnet add package Framework.PushNotifications.Dev
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

- `Framework.PushNotifications.Abstractions`

## Side Effects

- Registers `IPushNotificationService` as singleton
