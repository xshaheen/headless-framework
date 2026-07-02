# Headless.Sms.Cequens

Cequens SMS gateway implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via the Cequens API, a regional MENA gateway that authenticates via JWT token (obtained from API key + username).

## Key Features

- `CequensSmsSender` — implements `ISmsSender` (single recipient) and `IBulkSmsSender` (multi-recipient bulk), backed by the Cequens REST API.
- JWT token-based auth with automatic token acquisition from `TokenEndpoint`.
- Optional pre-configured `Token` to skip the sign-in flow.
- Configurable `SingleSmsEndpoint` and `TokenEndpoint` (defaults point to the Cequens production API).
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.

## Design Notes

The HTTP resilience handler is wired with `options.Retry.ShouldHandle = static _ => PredicateResult.False()` — no retries by default. SMS sends are not idempotent, and retrying a failed send without an idempotency key can deliver duplicate messages. Pass `configureResilience` to opt back in if Cequens provides idempotency support for your account. Each instance owns its own JWT token cache (an instance field on the sender), so a named instance never shares a token with the default sender or another name.

## Installation

```bash
dotnet add package Headless.Sms.Cequens
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseCequens(builder.Configuration.GetSection("Sms:Cequens")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseCequens(options =>
    {
        options.ApiKey = "your-api-key";
        options.UserName = "your-username";
        options.SenderName = "MyApp";
    })
);

// Named instance — an isolated HttpClient, token cache, and options (keyed "otp"):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseCequens(builder.Configuration.GetSection("Sms:Cequens")); // default (required)
    setup.AddNamed("otp", i => i.UseCequens(builder.Configuration.GetSection("Sms:CequensOtp")));
});
```

## Configuration

### appsettings.json

```json
{
  "Sms": {
    "Cequens": {
      "ApiKey": "your-api-key",
      "UserName": "your-username",
      "SenderName": "MyApp",
      "SingleSmsEndpoint": "https://apis.cequens.com/sms/v1/messages",
      "TokenEndpoint": "https://apis.cequens.com/auth/v1/tokens"
    }
  }
}
```

### Options

| Option | Type | Required | Default | Description |
|---|---|---|---|---|
| `ApiKey` | `string` | Yes | — | Cequens API key for token acquisition. |
| `UserName` | `string` | Yes | — | Cequens account username. |
| `SenderName` | `string` | Yes | — | Sender name shown to recipients. |
| `SingleSmsEndpoint` | `string` | No | `https://apis.cequens.com/sms/v1/messages` | Override for non-default environments. |
| `TokenEndpoint` | `string` | No | `https://apis.cequens.com/auth/v1/tokens` | Override for non-default environments. |
| `Token` | `string?` | No | `null` | Pre-issued JWT; skips sign-in if set. |

## Dependencies

- `Headless.Sms.Core`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Default: registers `ISmsSender` (`CequensSmsSender`) and `IBulkSmsSender` (forwarding to the same instance) as unkeyed singletons, plus a named `HttpClient` (`Headless:CequensSms`) with a standard resilience handler (retry disabled).
- Named (`AddNamed(name, i => i.UseCequens(…))`): registers a keyed `ISmsSender` and keyed `IBulkSmsSender` (same instance), named options, and a per-name `HttpClient` (`Headless:CequensSms:{name}`) with its own resilience pipeline — so each named sender owns an isolated token cache and never reads another instance's settings.
