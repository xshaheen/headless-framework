# Headless.Sms.Connekio

Connekio SMS gateway implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via the Connekio API using basic username/password/accountId authentication, supporting both single-message and batch delivery.

## Key Features

- `ConnekioSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk).
- Separate `SingleSmsEndpoint` (used by `SendAsync`) and `BatchSmsEndpoint` (used by `SendBulkAsync`).
- Basic auth: `UserName` + `Password` + `AccountId`.
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.

## Design Notes

Retry is disabled by default for the same reason as all HTTP SMS providers: sending the same message twice can cause duplicate delivery. Pass `configureResilience` to opt back in if Connekio assigns idempotency keys for your account tier.

## Installation

```bash
dotnet add package Headless.Sms.Connekio
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseConnekio(builder.Configuration.GetSection("Sms:Connekio")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseConnekio(options =>
    {
        options.UserName = "your-username";
        options.Password = "your-password";
        options.AccountId = "your-account-id";
        options.Sender = "MyApp";
    })
);
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Connekio": {
      "UserName": "your-username",
      "Password": "your-password",
      "AccountId": "your-account-id",
      "Sender": "MyApp",
      "SingleSmsEndpoint": "https://api.connekio.com/sms/single",
      "BatchSmsEndpoint": "https://api.connekio.com/sms/batch"
    }
  }
}
```

### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `UserName` | `string` | Yes | — | Connekio account username. |
| `Password` | `string` | Yes | — | Connekio account password. |
| `AccountId` | `string` | Yes | — | Connekio account identifier. |
| `Sender` | `string` | Yes | — | Sender name shown to recipients. |
| `SingleSmsEndpoint` | `string` | No | `https://api.connekio.com/sms/single` | Override for non-default environments. |
| `BatchSmsEndpoint` | `string` | No | `https://api.connekio.com/sms/batch` | Override for non-default environments. |

## Dependencies

- `Headless.Sms.Abstractions`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Registers `ISmsSender` as singleton (`ConnekioSmsSender`).
- Registers a named `HttpClient` (`Headless:ConnekioSms`) with a standard resilience handler (retry disabled).
