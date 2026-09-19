# Headless.PushNotifications.Firebase

Firebase Cloud Messaging (FCM) implementation of `IPushNotificationService` for production push notifications.

## Why use this package

Delivers push notifications to Android (FCM), iOS (via FCM-to-APNs bridge), and Web clients using the FCM v1 API. Handles multicast batching, transient-error retry with exponential backoff, and per-FID outcome mapping behind the `IPushNotificationService` interface.

## Install

```bash
dotnet add package Headless.PushNotifications.Firebase
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Push Notifications guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/push-notifications.md#headlesspushnotificationsfirebase)
