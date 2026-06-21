# Headless.Sms.Vodafone

Vodafone Egypt enterprise SMS gateway implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via the Vodafone Egypt enterprise messaging API, which uses a shared-secret (`SecureHash`) authentication model alongside account credentials.

## Key Features

- `VodafoneSmsSender` — `ISmsSender` implementation backed by the Vodafone Egypt REST API.
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

builder.Services.AddVodafoneSmsSender(
    builder.Configuration.GetSection("Sms:Vodafone")
);

// Or in code:
builder.Services.AddVodafoneSmsSender(options =>
{
    options.AccountId = "your-account-id";
    options.Password = "your-password";
    options.SecureHash = "your-secure-hash";
    options.Sender = "MyApp";
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
| `SendSmsEndpoint` | `string` | Yes | `https://e3len.vodafone.com.eg/web2sms/sms/submit/` | Override for non-default environments. |

## Dependencies

- `Headless.Sms.Abstractions`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Registers `ISmsSender` as singleton (`VodafoneSmsSender`).
- Registers a named `HttpClient` (`Headless:VodafoneSms`) with a standard resilience handler (retry disabled).
