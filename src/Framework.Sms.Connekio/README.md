# Framework.Sms.Connekio

This package integrates Connekio SMS services.

## Features

-   **ConnekioSmsSender**: Sends SMS via Connekio API.
-   **Configuration**: Uses `ConnekioSmsOptions` for credentials.
-   **Setup**: `AddConnekioSmsExtensions` for DI registration.

## Usage

### Configuration

Ensure your configuration includes the necessary Connekio credentials (username, password, account ID, etc.).

### Registration

```csharp
services.AddConnekioSms(configuration);
```

### Sending

Resolving `ISmsSender` will provide the Connekio implementation.
