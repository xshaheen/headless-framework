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
