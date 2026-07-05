# Headless.Sms.Twilio

Twilio SMS implementation of `ISmsSender`.

## Problem Solved

Provides SMS sending via Twilio's REST API, the most widely supported international SMS platform, with configurable sender number and optional per-message price cap.

## Key Features

- `TwilioSmsSender` — `ISmsSender` implementation using `ITwilioRestClient`. Single recipient per send; does not implement `IBulkSmsSender` (Twilio creates one message per recipient).
- `Sid` + `AuthToken` — Twilio account credentials.
- `PhoneNumber` — E.164 sender number validated by `InternationalPhoneNumber` rule.
- `MaxPrice` — optional per-message USD price cap.
- `Region` + `Edge` — optional Twilio region/edge node selection for data residency or latency.
- Standard resilience pipeline with auto-retry **disabled** by default to prevent duplicate SMS.
- Optional `configureClient` and `configureResilience` hooks for fine-grained `HttpClient` control.
- Cancellation is honored up to the point of dispatch only: the Twilio SDK (7.x) does not accept a `CancellationToken` on its send path, so an already-cancelled token throws before the call, but cancellation mid-flight cannot interrupt the in-progress request.

## Installation

```bash
dotnet add package Headless.Sms.Twilio
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHeadlessSms(setup => setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")));

// Or in code:
builder.Services.AddHeadlessSms(setup =>
    setup.UseTwilio(options =>
    {
        options.Sid = "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
        options.AuthToken = "your-auth-token";
        options.PhoneNumber = "+12025551234";
    })
);

// Named instance — a keyed ISmsSender plus a keyed ITwilioRestClient (resolvable via ISmsSenderProvider):
builder.Services.AddHeadlessSms(setup =>
{
    setup.UseTwilio(builder.Configuration.GetSection("Sms:Twilio")); // default (optional)
    setup.AddNamed("marketing", i => i.UseTwilio(builder.Configuration.GetSection("Sms:TwilioMarketing")));
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

- `Headless.Sms.Core`
- `Twilio`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Default: registers `ITwilioRestClient` via `TryAddSingleton` (a host-supplied client wins), `ISmsSender` (`TwilioSmsSender`) as an unkeyed singleton, and a named `HttpClient` (`Headless:TwilioSms`) with a standard resilience handler (retry disabled). No `IBulkSmsSender` — Twilio creates one message per recipient.
- Named (`AddNamed(name, i => i.UseTwilio(…))`): registers a keyed `ITwilioRestClient` (built from that name's options and per-name HttpClient), a keyed `ISmsSender`, named options, and a per-name `HttpClient` (`Headless:TwilioSms:{name}`) with its own resilience pipeline.
