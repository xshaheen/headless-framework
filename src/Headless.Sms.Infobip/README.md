# Headless.Sms.Infobip

Infobip global SMS platform implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via Infobip's REST API, a global messaging platform with delivery reporting and per-account regional base paths.

## Key Features

- `InfobipSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk, with per-recipient message ids), backed by the Infobip REST API.
- API key authentication via HTTP `Authorization` header.
- `BasePath` — Infobip-assigned base URL for your account (varies per account; not a shared endpoint).
- `Sender` — alphanumeric or numeric sender shown to recipients.
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.

## Installation

```bash
dotnet add package Headless.Sms.Infobip
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseInfobip(builder.Configuration.GetSection("Sms:Infobip")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseInfobip(options =>
    {
        options.ApiKey = "your-api-key";
        options.BasePath = "https://XXXXXXXX.api.infobip.com"; // account-specific URL
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "marketing"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseInfobip(builder.Configuration.GetSection("Sms:Infobip")); // default (optional)
    setup.AddNamed("marketing", i => i.UseInfobip(builder.Configuration.GetSection("Sms:InfobipBulk")));
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Infobip": {
      "ApiKey": "your-api-key",
      "BasePath": "https://XXXXXXXX.api.infobip.com",
      "Sender": "MyApp"
    }
  }
}
```

### Options

| Option | Type | Required | Description |
|---|---|---|---|
| `ApiKey` | `string` | Yes | Infobip API key for bearer authentication. |
| `BasePath` | `string` | Yes | Account-specific Infobip base URL (must be HTTPS). |
| `Sender` | `string` | Yes | Sender name or number shown to recipients. |

## Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Default: registers `ISmsSender` (`InfobipSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:InfobipSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseInfobip(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:InfobipSms:{name}`) with its own resilience pipeline.
