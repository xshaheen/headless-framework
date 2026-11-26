# Headless.PushNotifications.Dev

Development push notification providers that capture or discard notifications instead of sending them.

## Why use this package

Prevents real notifications from being sent during development or test runs, and keeps the captured ones in memory so a dev inbox or a test can inspect them. Uses the same `IPushNotificationService` interface as production so no application code changes are needed when switching environments.

## Install

```bash
dotnet add package Headless.PushNotifications.Dev
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Push Notifications guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/push-notifications.md#headlesspushnotificationsdev)
