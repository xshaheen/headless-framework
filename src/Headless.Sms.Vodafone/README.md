# Headless.Sms.Vodafone

Vodafone Egypt enterprise SMS gateway implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via the Vodafone Egypt enterprise messaging API, which uses a shared-secret (`SecureHash`) authentication model alongside account credentials.

## Key Features

- `VodafoneSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk), backed by the Vodafone Egypt REST API.
- Account credentials: `AccountId` + `Password` + `SecureHash`.
- Configurable `Sender` name and `SendSmsEndpoint`.
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.

## Design Notes

Vodafone Egypt's API requires a `SecureHash` in addition to account credentials — this is not an OAuth2 or JWT flow. The hash is issued by Vodafone at account provisioning and must be stored as a secret. Do not confuse this provider with a generic Vodafone API; the endpoint defaults to `https://e3len.vodafone.com.eg/web2sms/sms/submit/`.

## Installation

```bash
dotnet add package Headless.Sms.Vodafone
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseVodafone(builder.Configuration.GetSection("Sms:Vodafone")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseVodafone(options =>
    {
        options.AccountId = "your-account-id";
        options.Password = "your-password";
        options.SecureHash = "your-secure-hash";
        options.Sender = "MyApp";
    })
);

// Named instance — an isolated HttpClient and options (keyed "promo"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseVodafone(builder.Configuration.GetSection("Sms:Vodafone")); // default (optional)
    setup.AddNamed("promo", i => i.UseVodafone(builder.Configuration.GetSection("Sms:VodafonePromo")));
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Vodafone": {
      "AccountId": "your-account-id",
      "Password": "your-password",
      "SecureHash": "your-secure-hash",
      "Sender": "MyApp",
      "SendSmsEndpoint": "https://e3len.vodafone.com.eg/web2sms/sms/submit/"
    }
  }
}
```

### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `AccountId` | `string` | Yes | — | Vodafone Egypt account identifier. |
| `Password` | `string` | Yes | — | Vodafone Egypt account password. |
| `SecureHash` | `string` | Yes | — | Shared secret issued at provisioning. |
| `Sender` | `string` | Yes | — | Sender name shown to recipients. |
| `SendSmsEndpoint` | `string` | No | `https://e3len.vodafone.com.eg/web2sms/sms/submit/` | Override for non-default environments. |

## Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Default: registers `ISmsSender` (`VodafoneSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:VodafoneSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseVodafone(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:VodafoneSms:{name}`) with its own resilience pipeline.
