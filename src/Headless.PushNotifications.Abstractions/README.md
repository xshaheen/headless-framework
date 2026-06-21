# Headless.PushNotifications.Abstractions

Defines the unified interface and contract types for push notification services.

## Problem Solved

Provides a provider-agnostic push notification API so application code never depends on a specific backend. Switching from the dev no-op to Firebase for production requires only a DI registration change.

## Key Features

- `IPushNotificationService` — core sending interface:
  - `SendToDeviceAsync(clientToken, title, body, data?, ct)` — single-device delivery
  - `SendMulticastAsync(clientTokens, title, body, data?, ct)` — batch delivery
- `PushNotificationResponse` — single-device outcome with three states (`Success`, `Failure`, `Unregistered`); factory methods `Succeeded`, `Failed`, `Unregistered`; query methods `IsSucceeded()`, `IsFailed()`, `IsUnregistered()`; properties `Token`, `MessageId?`, `FailureError?`, `Status`
- `PushNotificationResponseStatus` enum — `Success`, `Failure`, `Unregistered`
- `BatchPushNotificationResponse` — multicast aggregate: `SuccessCount`, `FailureCount`, `Responses` (one per token)
- `HeadlessPushNotificationsSetupBuilder` — builder used by `AddHeadlessPushNotifications` to select exactly one provider
- `IPushNotificationsProviderOptionsExtension` — hook implemented by each provider package to register its services; exposed so third-party providers can integrate

## Installation

```bash
dotnet add package Headless.PushNotifications.Abstractions
```

## Quick Start

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

## Configuration

None. This is an abstractions-only package.

## Dependencies

None.

## Side Effects

None. This package defines only interfaces and contracts.
