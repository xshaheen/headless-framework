# Headless.PushNotifications.Core

Setup builder, registration gates, and the named-service provider for the push-notifications abstraction.

## Why use this package

Owns the unified push-notifications setup builder (`AddHeadlessPushNotifications`) and the `IPushNotificationServiceProvider` implementation, giving every provider one registration grammar (a default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

## Install

```bash
dotnet add package Headless.PushNotifications.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Push Notifications guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/push-notifications.md#headlesspushnotificationscore)
