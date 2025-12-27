# Framework.Sms.VictoryLink

This package provides an implementation of the SMS abstraction for the VictoryLink SMS gateway.

## Features

-   **VictoryLinkSmsSender**: Implements `ISmsSender` to send messages using VictoryLink's services.
-   **Configuration**: Uses `VictoryLinkSmsOptions` for setting up account details.
-   **Dependency Injection**: Easy registration via `AddVictoryLinkExtensions`.

## Usage

### Configuration

Add the configuration section for `VictoryLinkSms` (structure depends on `VictoryLinkSmsOptions` properties, typically includes username, password, sender, etc.).

### Registration

```csharp
services.AddVictoryLinkSms(configuration);
```

### Service Injection

Inject `ISmsSender` to use the VictoryLink implementation in your application logic.
