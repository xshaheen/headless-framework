# Framework.Sms.Twilio

Twilio SMS implementation.

## Problem Solved

Provides SMS sending via Twilio's messaging API with support for sender numbers and messaging service SIDs.

## Key Features

- `TwilioSmsSender` - ISmsSender implementation using Twilio
- Account SID and Auth Token authentication
- Configurable sender phone number
- Messaging Service SID support

## Installation

```bash
dotnet add package Framework.Sms.Twilio
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTwilioSmsSender(options =>
{
    options.AccountSid = "your-account-sid";
    options.AuthToken = "your-auth-token";
    options.From = "+1234567890";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Twilio": {
      "AccountSid": "your-account-sid",
      "AuthToken": "your-auth-token",
      "From": "+1234567890"
    }
  }
}
```

### Code Configuration

```csharp
builder.Services.AddTwilioSmsSender(options =>
{
    options.AccountSid = config["Twilio:AccountSid"]!;
    options.AuthToken = config["Twilio:AuthToken"]!;
    options.From = config["Twilio:From"]!;
    // Or use MessagingServiceSid instead of From
    options.MessagingServiceSid = config["Twilio:MessagingServiceSid"];
});
```

## Dependencies

- `Framework.Sms.Abstractions`
- `Twilio`

## Side Effects

- Registers `ISmsSender` as singleton
