# Headless.PushNotifications.Dev

No-op push notification provider for local development and testing.

## Why use this package

Prevents real notifications from being sent during development or test runs. Uses the same `IPushNotificationService` interface as production so no application code changes are needed when switching environments.

## Install

```bash
dotnet add package Headless.PushNotifications.Dev
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Push Notifications guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/push-notifications.md#headlesspushnotificationsdev)
