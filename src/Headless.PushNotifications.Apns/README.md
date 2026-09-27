# Headless.PushNotifications.Apns

Apple Push Notification service (APNs) implementation of `IPushNotificationService` for iOS push notifications without a Firebase project.

## Why use this package

Delivers alert and VoIP notifications straight to APNs over HTTP/2 with token-based (`.p8` key) authentication. Reports device tokens Apple has retired as `Unregistered`, the same status the Firebase provider uses, so token cleanup code works with either provider. Use Firebase instead when one sender must also reach Android or Web clients.

## Install

```bash
dotnet add package Headless.PushNotifications.Apns
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Push Notifications guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/push-notifications.md#headlesspushnotificationsapns)
