# Framework.Sms.Vodafone

This package provides an implementation of the SMS abstraction for the Vodafone SMS gateway.

## Features

-   **VodafoneSmsSender**: Implements `ISmsSender` to send messages using Vodafone's API.
-   **Configuration**: Uses `VodafoneSmsOptions` for configuring API credentials and endpoints.
-   **Dependency Injection**: Provides extension methods to easily register services in the DI container.

## Usage

### Configuration

Configure the `VodafoneSmsOptions` in your `appsettings.json` or other configuration source:

```json
{
    "VodafoneSms": {
        "ClientId": "your-client-id",
        "ClientSecret": "your-client-secret",
        "SenderName": "YourSender",
        "BaseUrl": "https://..."
    }
}
```

### Registration

Use the extension method in your startup code:

```csharp
services.AddVodafoneSms(configuration);
```

### Sending SMS

Inject `ISmsSender` into your services:

```csharp
public class MyService
{
    private readonly ISmsSender _smsSender;

    public MyService(ISmsSender smsSender)
    {
        _smsSender = smsSender;
    }

    public async Task SendAlertAsync(string phoneNumber, string message)
    {
        var request = new SendSingleSmsRequest
        {
            MobileNumber = phoneNumber,
            Message = message
        };

        var response = await _smsSender.SendAsync(request);
    }
}
```
