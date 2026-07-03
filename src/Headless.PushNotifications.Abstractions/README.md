# Headless.PushNotifications.Abstractions

Defines the unified interface and contract types for push notification services.

## Problem Solved

Provides a provider-agnostic push notification API so application code never depends on a specific backend. Switching from the dev no-op to Firebase for production requires only a DI registration change.

## Key Features

- `IPushNotificationService` — core sending interface:
  - `SendToDeviceAsync(clientToken, title, body, data?, ct)` — single-device delivery
  - `SendMulticastAsync(clientTokens, title, body, data?, ct)` — batch delivery
- `IPushNotificationServiceProvider` — resolves named services by name: `GetService(name)` (throws when unregistered) and `GetServiceOrNull(name)` (returns `null`), plus `RegisteredNames` (`IReadOnlySet<string>`) listing the registered named instances (the default is excluded) so an externally supplied name can be validated before resolving. Backed by the container's keyed `IPushNotificationService` registrations; the concrete implementation lives in `Headless.PushNotifications.Core`.
- `PushNotificationResponse` — single-device outcome with three states (`Success`, `Failure`, `Unregistered`); factory methods `Succeeded`, `Failed`, `Unregistered`; query methods `IsSucceeded()`, `IsFailed()`, `IsUnregistered()`; properties `Token`, `MessageId?`, `FailureError?`, `Status`
- `PushNotificationResponseStatus` enum — `Success`, `Failure`, `Unregistered`
- `BatchPushNotificationResponse` — multicast aggregate: `SuccessCount`, `FailureCount`, `Responses` (one per token)

> Registration (`AddHeadlessPushNotifications`, `HeadlessPushNotificationsSetupBuilder`, and the `IPushNotificationServiceProvider` implementation) lives in `Headless.PushNotifications.Core`, pulled in transitively by each provider package.

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

    // To route to a named instance, depend on IPushNotificationServiceProvider and resolve by name
    // (provider.GetService("driver-app")), or inject [FromKeyedServices("driver-app")] IPushNotificationService.

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

- `Headless.Checks`

## Side Effects

None. This package defines only interfaces and contracts.
