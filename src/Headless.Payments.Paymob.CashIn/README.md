# Headless.Payments.Paymob.CashIn

Paymob Accept integration for cash-in (payment collection) operations.

## Problem Solved

Provides a typed client for the Paymob Accept payment gateway, supporting multiple collection channels (cards via iframe, mobile wallets, kiosk, cash collection) with HMAC validation for secure callback verification.

## Key Features

- `IPaymobCashInBroker` — main payment operations interface:
  - `CreateIntentionAsync` — v2 Payment Intentions API; returns `ClientSecret` for frontend use
  - `CreateOrderAsync` / `RequestPaymentKeyAsync` — legacy order/payment-key flow
  - `CreateWalletPayAsync` / `CreateKioskPayAsync` / `CreateCashCollectionPayAsync` / `CreateSavedTokenPayAsync` — channel-specific pay execution
  - `RefundTransactionAsync` / `VoidTransactionAsync` — post-capture operations
  - `GetTransactionAsync` / `GetTransactionsPageAsync` — transaction queries
  - `GetOrderAsync` / `GetOrdersPageAsync` — order queries
  - `Validate(...)` — four overloads for HMAC callback verification
  - `CreateIframeSrc(iframeId, token)` — builds the card iframe URL
- `IPaymobCashInAuthenticator` — caches the 60-minute auth token; refreshes 5 minutes before expiry
- `CashInCallbackTypes` — `TRANSACTION` and `TOKEN` callback type constants
- `CashInStatuses` — `pending`, `declined`, `success` constants
- `CashInBillingData` — requires first name, last name, phone number, and email in its constructor; optional address and shipping values are init-only properties that default to `"NA"`
- `PaymobCashInException` — thrown on non-success HTTP responses from Paymob

## Design Notes

`IPaymobCashInBroker` is registered as scoped (not singleton) because it takes a typed `HttpClient`. `IPaymobCashInAuthenticator` is singleton and holds the cached auth token; the broker calls the authenticator on each request. Options changes invalidate the cached token automatically via `IOptionsMonitor<PaymobCashInOptions>`.

The package adds `AddStandardResilienceHandler()` to the named HTTP client automatically. Pass `configureResilience` to `AddPaymobCashIn(...)` to override the resilience policy.

`PaymobCashInOptions.ToString()` is overridden to redact `ApiKey`, `Hmac`, and `SecretKey` (printed as `***`), so logging or diagnostics that stringify the options never leak the secrets.

All Paymob URL options require HTTPS for external hosts. HTTP is accepted only for loopback development/test servers, and URLs containing userinfo are rejected, so API credentials and payment tokens cannot be configured for remote plaintext transport.

## Installation

```bash
dotnet add package Headless.Payments.Paymob.CashIn
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option 1: delegate configuration
builder.Services.AddPaymobCashIn(options =>
{
    options.ApiKey = builder.Configuration["Paymob:ApiKey"]!;
    options.Hmac = builder.Configuration["Paymob:Hmac"]!;
    options.SecretKey = builder.Configuration["Paymob:SecretKey"]!;
});

// Option 2: IConfiguration section
builder.Services.AddPaymobCashIn(builder.Configuration.GetSection("Paymob:CashIn"));
```

Create a payment intention (v2 API — preferred):

```csharp
public sealed class PaymentService(IPaymobCashInBroker broker)
{
    public async Task<string> CreatePaymentAsync(long amountCents, long integrationId, CancellationToken ct)
    {
        var response = await broker.CreateIntentionAsync(
            new CashInCreateIntentionRequest
            {
                Amount = amountCents, // in cents, e.g. 10000 = 100 EGP
                Currency = "EGP",
                PaymentMethods = [integrationId],
                BillingData = new CashInCreateIntentionRequestBillingData
                {
                    FirstName = "Ahmed",
                    LastName = "Ali",
                    PhoneNumber = "+201001234567",
                    Email = "ahmed@example.com",
                },
                Items = [],
            },
            ct
        );

        return response!.ClientSecret; // pass to your frontend Paymob.js
    }

    public Task<CashInPaymentKeyResponse> CreateLegacyPaymentKeyAsync(
        long integrationId,
        long orderId,
        long amountCents,
        CancellationToken ct
    )
    {
        var billingData = new CashInBillingData("Ahmed", "Ali", "+201001234567", "ahmed@example.com")
        {
            Country = "EG",
            City = "Cairo",
            Street = "Tahrir Street",
        };

        return broker.RequestPaymentKeyAsync(
            new CashInPaymentKeyRequest(integrationId, orderId, billingData, amountCents),
            ct
        );
    }
}
```

Validate an incoming callback:

```csharp
[HttpPost("paymob/callback")]
public IActionResult HandleCallback([FromBody] CashInCallbackTransaction transaction, [FromQuery] string hmac)
{
    if (!_broker.Validate(transaction, hmac))
        return BadRequest("Invalid HMAC");

    if (transaction.Success)
    {
        // mark order as paid
    }

    return Ok();
}
```

## Configuration

```json
{
    "Paymob": {
        "CashIn": {
            "ApiKey": "your-api-key",
            "Hmac": "your-hmac-secret",
            "SecretKey": "your-secret-key",
            "ExpirationPeriod": 3600,
            "TokenRefreshBuffer": "00:55:00"
        }
    }
}
```

| Property | Required | Default | Description |
|---|---|---|---|
| `ApiKey` | Yes | — | Merchant API key for the legacy auth endpoint. |
| `Hmac` | Yes | — | Secret used to verify callback HMAC signatures. |
| `SecretKey` | Yes | — | Secret key for the v2 Intentions API. |
| `ExpirationPeriod` | No | `3600` | Payment token lifetime in seconds (must be > 60). |
| `TokenRefreshBuffer` | No | `00:55:00` | How early to refresh the auth token (must be < 60 min). |
| `ApiBaseUrl` | No | `https://accept.paymobsolutions.com/api` | Legacy API base URL. |
| `CreateIntentionUrl` | No | `https://accept.paymob.com/v1/intention/` | v2 Intentions endpoint. |
| `RefundUrl` | No | `https://accept.paymob.com/api/acceptance/void_refund/refund` | Refund endpoint. |
| `VoidRefundUrl` | No | `https://accept.paymob.com/api/acceptance/void_refund/void` | Void endpoint. |
| `IframeBaseUrl` | No | `https://accept.paymob.com/api/acceptance/iframes` | Card iframe base URL. |

## Dependencies

- `Headless.Extensions`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Registers `IPaymobCashInAuthenticator` as singleton
- Registers `IPaymobCashInBroker` as scoped with typed `HttpClient`
- Adds named `HttpClient` (`"Headless:PaymobCashIn"`) with standard resilience handler
