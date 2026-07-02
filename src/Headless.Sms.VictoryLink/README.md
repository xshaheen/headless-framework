# Headless.Sms.VictoryLink

VictoryLink SMS gateway implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via the VictoryLink API, a regional gateway serving the Middle East market with username/password authentication.

## Key Features

- `VictoryLinkSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk), backed by the VictoryLink REST API.
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

builder.Services.AddHeadlessSms(setup => setup.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLink")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseVictoryLink(options =>
    {
        options.UserName = "your-username";
        options.Password = "your-password";
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "otp"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLink")); // default (optional)
    setup.AddNamed("otp", i => i.UseVictoryLink(builder.Configuration.GetSection("Sms:VictoryLinkOtp")));
});
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

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Default: registers `ISmsSender` (`VictoryLinkSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:VictoryLinkSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseVictoryLink(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:VictoryLinkSms:{name}`) with its own resilience pipeline.
