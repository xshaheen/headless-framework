# Headless.Sms.VictoryLink

VictoryLink SMS gateway implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via the VictoryLink API, a regional gateway serving the Middle East market with username/password authentication.

## Key Features

- `VictoryLinkSmsSender` — `ISmsSender` implementation backed by the VictoryLink REST API.
- Username + password authentication.
- Configurable `Sender` name and `Endpoint` URL.
- Response-code-based error detection.
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.

## Installation

```bash
dotnet add package Headless.Sms.VictoryLink
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseVictoryLink(
    builder.Configuration.GetSection("Sms:VictoryLink")
));

// Or in code:
builder.Services.AddHeadlessSms(setup => setup.UseVictoryLink(options =>
{
    options.UserName = "your-username";
    options.Password = "your-password";
    options.Sender = "MyApp";
}));
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "VictoryLink": {
      "UserName": "your-username",
      "Password": "your-password",
      "Sender": "MyApp",
      "Endpoint": "https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS"
    }
  }
}
```

### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `UserName` | `string` | Yes | — | VictoryLink account username. |
| `Password` | `string` | Yes | — | VictoryLink account password. |
| `Sender` | `string` | Yes | — | Sender name shown to recipients. |
| `Endpoint` | `string` | No | VictoryLink production URL | Override for non-default environments. |

## Dependencies

- `Headless.Sms.Abstractions`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Registers `ISmsSender` as singleton (`VictoryLinkSmsSender`).
- Registers a named `HttpClient` (`Headless:VictoryLinkSms`) with a standard resilience handler (retry disabled).
