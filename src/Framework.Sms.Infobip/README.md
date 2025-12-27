# Framework.Sms.Infobip

This package provides support for Infobip SMS services.

## Features

-   **InfobipSmsSender**: The implementation of `ISmsSender` for Infobip.
-   **Configuration**: Managed via `InfobipOptions`.
-   **Easy Setup**: Extension methods provided in `AddInfobipExtensions`.

## Usage

### Configuration

Configure `InfobipOptions` with your API key and base URL.

### Registration

```csharp
services.AddInfobipSms(configuration);
```

### Interaction

Inject `ISmsSender` to send SMS messages through Infobip without coupling your code to the provider details.
