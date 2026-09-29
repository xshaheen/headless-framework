# Headless.PushNotifications.Apns

Apple Push Notification service (APNs) implementation of `IPushNotificationService` for iOS push notifications without a Firebase project.

## Why use this package

Delivers notifications straight to APNs over HTTP/2, authenticated with a `.p8` signing key or a `.p12` provider certificate. Beyond shared alerts, data-only messages, and VoIP pushes, `IApnsPushNotificationService` sends rich alerts, Live Activities, and the other APNs push types. Reports device tokens Apple has retired as `Unregistered`, the same status the Firebase provider uses, so token cleanup code works with either provider. Use Firebase instead when one sender must also reach Android or Web clients.

## Install

```bash
dotnet add package Headless.PushNotifications.Apns
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Push Notifications guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/push-notifications.md#headlesspushnotificationsapns)
