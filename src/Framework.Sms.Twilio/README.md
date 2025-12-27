# Framework.Sms.Twilio

This package integrates Twilio's SMS services into the framework.

## Features

-   **TwilioSmsSender**: Implements `ISmsSender` using the Twilio API.
-   **Configuration**: `TwilioSmsOptions` encapsulates settings like Account SID, Auth Token, and From number.
-   **DI Support**: Includes `AddTwilioExtensions` for seamless setup.

## Usage

### Configuration

Set up your Twilio credentials:

```json
{
    "Twilio": {
        "AccountSid": "...",
        "AuthToken": "...",
        "FromNumber": "..."
    }
}
```

### Registration

```csharp
services.AddTwilioSms(configuration);
```

### Sending Messages

Use the standard `ISmsSender` interface to send messages.
