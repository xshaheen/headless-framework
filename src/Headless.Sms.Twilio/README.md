# Headless.Sms.Twilio

Twilio SMS implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via Twilio's REST API, the most widely supported international SMS platform, with configurable sender number and optional per-message price cap.

## Key Features

- `TwilioSmsSender` — `ISmsSender` implementation using `ITwilioRestClient`.
- `Sid` + `AuthToken` — Twilio account credentials.
- `PhoneNumber` — E.164 sender number validated by `InternationalPhoneNumber` rule.
- `MaxPrice` — optional per-message USD price cap.
- `Region` + `Edge` — optional Twilio region/edge node selection for data residency or latency.
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.

## Installation

```bash
dotnet add package Headless.Sms.Twilio
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTwilioSmsSender(
    builder.Configuration.GetSection("Sms:Twilio")
);

// Or in code:
builder.Services.AddTwilioSmsSender(options =>
{
    options.Sid = "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
    options.AuthToken = "your-auth-token";
    options.PhoneNumber = "+12025551234";
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Twilio": {
      "Sid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
      "AuthToken": "your-auth-token",
      "PhoneNumber": "+12025551234",
      "MaxPrice": null,
      "Region": null,
      "Edge": null
    }
  }
}
```

### Options

| Option | Type | Required | Description |
|---|---|---|---|
| `Sid` | `string` | Yes | Twilio Account SID (`AC...`). |
| `AuthToken` | `string` | Yes | Twilio Auth Token. |
| `PhoneNumber` | `string` | Yes | E.164 sender number (e.g. `+12025551234`). |
| `MaxPrice` | `decimal?` | No | Maximum USD price per message. Twilio rejects if exceeded. |
| `Region` | `string?` | No | Twilio region for data residency (e.g. `au1`, `ie1`). |
| `Edge` | `string?` | No | Twilio edge node (e.g. `sydney`, `dublin`). |

## Dependencies

- `Headless.Sms.Abstractions`
- `Twilio`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Registers `ITwilioRestClient` as singleton (backed by the named `HttpClient`).
- Registers `ISmsSender` as singleton (`TwilioSmsSender`).
- Registers a named `HttpClient` (`Headless:TwilioSms`) with a standard resilience handler (retry disabled).
