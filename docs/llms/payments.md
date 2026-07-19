---
domain: Payments
packages: Payments.Paymob.CashIn, Payments.Paymob.CashOut, Payments.Paymob.Services
---

# Payments

## Table of Contents

- [Quick Orientation](#quick-orientation)
- [Agent Instructions](#agent-instructions)
- [Core Concepts](#core-concepts)
- [Headless.Payments.Paymob.CashIn](#headlesspaymentspaymobcashin)
    - [Problem Solved](#problem-solved)
    - [Key Features](#key-features)
    - [Design Notes](#design-notes)
    - [Installation](#installation)
    - [Quick Start](#quick-start)
    - [Configuration](#configuration)
    - [Dependencies](#dependencies)
    - [Side Effects](#side-effects)
- [Headless.Payments.Paymob.CashOut](#headlesspaymentspaymobcashout)
    - [Problem Solved](#problem-solved-1)
    - [Key Features](#key-features-1)
    - [Design Notes](#design-notes-1)
    - [Installation](#installation-1)
    - [Quick Start](#quick-start-1)
    - [Configuration](#configuration-1)
    - [Dependencies](#dependencies-1)
    - [Side Effects](#side-effects-1)
- [Headless.Payments.Paymob.Services](#headlesspaymentspaymobservices)
    - [Problem Solved](#problem-solved-2)
    - [Key Features](#key-features-2)
    - [Installation](#installation-2)
    - [Quick Start](#quick-start-2)
    - [Configuration](#configuration-2)
    - [Dependencies](#dependencies-2)
    - [Side Effects](#side-effects-2)

> Paymob Accept payment gateway integration for Egypt/MENA: payment collection (CashIn), disbursements (CashOut), and higher-level service workflows.

## Quick Orientation

Three packages cover different functions — they are not alternatives:

| Package | Job |
|---|---|
| `Headless.Payments.Paymob.CashIn` | Collect payments from customers (cards, wallets, kiosk, cash). |
| `Headless.Payments.Paymob.CashOut` | Disburse funds to bank accounts, wallets, and Aman kiosk. |
| `Headless.Payments.Paymob.Services` | Higher-level service layer on top of CashIn and CashOut with typed per-channel request/response models, fees calculation, and error mapping. |

Install only the packages that match your job. `Services` depends on both `CashIn` and `CashOut` and can replace direct broker usage for most application code.

Register via:
- `services.AddPaymobCashIn(options => ...)` — for payment collection.
- `services.AddPaymobCashOut(options => ...)` — for disbursements.
- `services.AddPaymobServices()` — registers `IPaymobCashInService`, `ICashOutService`, and `IPaymobCashInFeesCalculator` from the Services package (call it after the two broker registrations above).

## Agent Instructions

- Use `IPaymobCashInService` (from `Headless.Payments.Paymob.Services`) for most card/wallet/kiosk cash-in flows. Only drop to `IPaymobCashInBroker` directly when you need raw Paymob API access (order management, legacy payment-key flow).
- Use `ICashOutService` (from `Headless.Payments.Paymob.Services`) for typed disbursement flows. It translates each channel's request into the correct `CashOutDisburseRequest` factory call and maps status/error codes.
- CashIn and CashOut have **separate** API credentials. `PaymobCashInOptions` requires `ApiKey`, `Hmac`, and `SecretKey`. `PaymobCashOutOptions` requires `ApiBaseUrl`, `UserName`, `Password`, `ClientId`, and `ClientSecret`.
- Never store credentials in appsettings directly — always bind from secrets or environment variables.
- Always validate HMAC on CashIn callbacks. The broker exposes four `Validate(...)` overloads: for `CashInCallbackTransaction`, `CashInCallbackToken`, `CashInCallbackQueryParameters`, or a raw concatenated string. Choose by what Paymob sends to your endpoint.
- Money amounts denominated in cents are typed as `long` throughout CashIn (`CashInCreateIntentionRequest.Amount`, `CreateOrderRequest.AmountCents`, `PaymentKeyRequest.AmountCents`, and the response DTOs). For the legacy payment-key flow (`CreateOrderAsync` / `RequestPaymentKeyAsync`), multiply your decimal EGP amount by 100 yourself; the Services layer does this conversion internally. `CashOutDisburseRequest.Amount` and `CashOutTransaction.Amount` are `decimal` EGP (whole-currency, not cents).
- Paymob-assigned resource identifiers are typed as `long` (`long?` when nullable) across every request/response DTO — transaction, order, integration, profile, owner, merchant, and gateway-integration IDs, plus kiosk/Aman bill references (`payment_methods` is `IReadOnlyList<long>`). They grow unboundedly and can exceed `int` range, so treat them as 64-bit everywhere. Genuinely bounded domain numbers stay `int` (quantities, package counts, shipping dimensions, status codes, `exp`/`expiration` seconds).
- CashOut `DisburseAsync(...)` (with the standard async suffix) is the broker method. Use `CashOutDisburseRequest` static factory methods — `BankCard`, `Vodafone`, `Etisalat`, `Orange`, `BankWallet`, `Accept` — rather than constructing the record directly.
- Both brokers (`IPaymobCashInBroker`, `IPaymobCashOutBroker`) are registered as **scoped** with an injected `HttpClient`. Do not treat them as singletons.
- `IPaymobCashInAuthenticator` and `IPaymobCashOutAuthenticator` are singletons that cache tokens in memory. They handle token refresh automatically (CashIn refreshes 5 minutes before the 60-minute Paymob token expiry; CashOut caches tokens for `TokenRefreshBuffer`, default 10 minutes).
- `CashOutTransaction.IsSuccess()`, `.IsFailed()`, `.IsPending()` are helper methods that interpret the `DisbursementStatus` / `StatusCode` pair — use them instead of raw string comparisons.
- This is a Paymob-specific integration (Egypt/MENA). There is no generic payment abstraction.

## Core Concepts

**Cash-in vs. cash-out**: cash-in means collecting money from a customer; cash-out means sending money to a recipient. They are entirely separate Paymob APIs with separate credentials, base URLs, and authentication flows.

**Paymob CashIn authentication**: the legacy flow uses an API key to obtain a 60-minute auth token, which is then used to create an order, request a payment key, and redirect the user to an iframe or deep link. The modern flow (v2) uses a `SecretKey` to call the Payment Intentions API directly and returns a `ClientSecret` that the frontend uses. Use `CreateIntentionAsync` for new integrations.

**Paymob CashOut authentication**: uses OAuth 2.0 password grant (`UserName` + `Password`) with a `ClientId`/`ClientSecret` Basic auth header to obtain a Bearer token. The token is cached and refreshed automatically by `IPaymobCashOutAuthenticator`.

**HMAC callback validation**: Paymob POSTs transaction callbacks to your endpoint and includes an HMAC query parameter. You must compute the HMAC over a canonical concatenated string of transaction fields using your `Hmac` secret and compare with the received value. Never trust a callback that fails `IPaymobCashInBroker.Validate(...)`.

**Payment channels (CashIn)**: each payment method requires its own Paymob integration ID configured in the merchant dashboard. Card payments use an iframe; wallets redirect the user to a deep link; kiosk generates a billing reference the customer presents at a physical kiosk; cash collection generates a reference for Fawry/similar networks.

**Disbursement channels (CashOut)**: each channel (Vodafone, Etisalat, Orange, bank wallet, bank card/IBAN, Aman kiosk) maps to a specific `Issuer` string in the Paymob request. The `CashOutDisburseRequest` factory methods enforce the required field set per channel.

---

## Headless.Payments.Paymob.CashIn

Paymob Accept integration for cash-in (payment collection) operations.

### Problem Solved

Provides a typed client for the Paymob Accept payment gateway, supporting multiple collection channels (cards via iframe, mobile wallets, kiosk, cash collection) with HMAC validation for secure callback verification.

### Key Features

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

### Design Notes

`IPaymobCashInBroker` is registered as scoped (not singleton) because it takes a typed `HttpClient`. `IPaymobCashInAuthenticator` is singleton and holds the cached auth token; the broker calls the authenticator on each request. Options changes invalidate the cached token automatically via `IOptionsMonitor<PaymobCashInOptions>`.

The package adds `AddStandardResilienceHandler()` to the named HTTP client automatically. Pass `configureResilience` to `AddPaymobCashIn(...)` to override the resilience policy (e.g., adjust timeouts for wallet redirects).

`PaymobCashInOptions.ToString()` is overridden to redact `ApiKey`, `Hmac`, and `SecretKey` (printed as `***`), so logging or diagnostics that stringify the options never leak the secrets.

All Paymob URL options require HTTPS for external hosts. HTTP is accepted only for loopback development/test servers, and URLs containing userinfo are rejected, so API credentials and payment tokens cannot be configured for remote plaintext transport.

### Installation

```bash
dotnet add package Headless.Payments.Paymob.CashIn
```

### Quick Start

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

### Configuration

#### appsettings.json

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

#### Options (`PaymobCashInOptions`)

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

### Dependencies

- `Headless.Extensions`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Registers `IPaymobCashInAuthenticator` as singleton
- Registers `IPaymobCashInBroker` as scoped with typed `HttpClient`
- Adds named `HttpClient` (`"Headless:PaymobCashIn"`) with standard resilience handler

---

## Headless.Payments.Paymob.CashOut

Paymob integration for cash-out (disbursement) operations.

### Problem Solved

Provides a typed client for the Paymob disbursement API, enabling payouts to bank accounts (via IBAN or card number), Egyptian mobile wallets (Vodafone, Etisalat, Orange), bank wallets, and Aman cash pickup.

### Key Features

- `IPaymobCashOutBroker` — disbursement operations interface:
  - `DisburseAsync(request)` — execute disbursement, returns `CashOutTransaction`
  - `GetBudgetAsync()` — query available balance, returns `CashOutBudgetResponse` (rate-limited to 5 req/min)
  - `GetTransactionsAsync(ids, isBankTransactions, page)` — paginated transaction lookup, returns `CashOutGetTransactionsResponse`
- `IPaymobCashOutAuthenticator` — OAuth2 password-grant token management with in-memory caching
- `CashOutDisburseRequest` — factory methods per channel:
  - `CashOutDisburseRequest.BankCard(amount, cardNumber, bankCode, transactionType, fullName)`
  - `CashOutDisburseRequest.BankWallet(amount, phoneNumber, fullName)`
  - `CashOutDisburseRequest.Vodafone(amount, phoneNumber)`
  - `CashOutDisburseRequest.Etisalat(amount, phoneNumber)`
  - `CashOutDisburseRequest.Orange(amount, phoneNumber, fullName)`
  - `CashOutDisburseRequest.Accept(amount, phoneNumber, firstName, lastName, email)` — Aman kiosk
- `BankTransactionTypes` — `CashTransfer`, `CreditCard`, `PrepaidCard`, `Salary` constants
- `CashOutTransaction` — result with status helper methods:
  - `IsSuccess()`, `IsFailed()`, `IsPending()`, `IsProviderDownError()`, `IsAuthenticationError()`
  - `IsNotHaveVodafoneCashError()`, `IsNotHaveEtisalatCashError()`, `IsRequestValidationError()`
- `CashOutBudgetResponse` — budget inquiry result; Paymob reports the balance as a human-readable sentence in `CurrentBudget` (e.g. `"Your current budget is 888.25 LE"`)
- `CashOutGetTransactionsResponse` — paginated transaction inquiry result (`Count`, `Next`, `Previous`, `Results`)
- `PaymobCashOutException` — thrown on non-success HTTP responses

### Design Notes

`IPaymobCashOutBroker` is registered as scoped with a typed `HttpClient`. The broker method is `DisburseAsync(...)` (standard async naming). `IPaymobCashOutAuthenticator` is singleton and caches the Bearer token; on options change, the cached token is invalidated automatically.

The CashOut authentication uses OAuth2 password grant, unlike CashIn's proprietary API-key flow. Credentials include `ClientId`/`ClientSecret` for Basic auth on the token endpoint, plus `UserName`/`Password` as the grant body. `TokenRefreshBuffer` (default 10 min) controls how far ahead of expiry to renew.

`ApiBaseUrl` requires HTTPS for external hosts. HTTP is accepted only for loopback development/test servers, and URLs containing userinfo are rejected, so OAuth credentials cannot be configured for remote plaintext transport.

### Installation

```bash
dotnet add package Headless.Payments.Paymob.CashOut
```

### Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaymobCashOut(options =>
{
    options.ApiBaseUrl = "https://disbursement.paymob.com/api/";
    options.UserName = builder.Configuration["Paymob:CashOut:UserName"]!;
    options.Password = builder.Configuration["Paymob:CashOut:Password"]!;
    options.ClientId = builder.Configuration["Paymob:CashOut:ClientId"]!;
    options.ClientSecret = builder.Configuration["Paymob:CashOut:ClientSecret"]!;
});
```

Disburse to a mobile wallet:

```csharp
public sealed class DisbursementService(IPaymobCashOutBroker broker)
{
    public async Task<CashOutTransaction> DisburseToVodafoneAsync(
        decimal amount,
        string phoneNumber,
        CancellationToken ct
    )
    {
        var request = CashOutDisburseRequest.Vodafone(amount, phoneNumber);
        var result = await broker.DisburseAsync(request, ct);

        if (result.IsFailed())
        {
            throw new InvalidOperationException(
                $"Disbursement failed: {result.DisbursementStatus} ({result.StatusCode})"
            );
        }

        return result;
    }
}
```

Disburse to a bank account:

```csharp
var request = CashOutDisburseRequest.BankCard(
    amount: 500m,
    cardNumber: "1234567890123456", // IBAN or card number
    bankCode: "CIB",
    transactionType: BankTransactionTypes.CashTransfer,
    fullName: "Ahmed Ali"
);
var result = await broker.DisburseAsync(request, cancellationToken);
```

### Configuration

#### appsettings.json

```json
{
    "Paymob": {
        "CashOut": {
            "ApiBaseUrl": "https://disbursement.paymob.com/api/",
            "UserName": "your-username",
            "Password": "your-password",
            "ClientId": "your-client-id",
            "ClientSecret": "your-client-secret",
            "TokenRefreshBuffer": "00:10:00"
        }
    }
}
```

#### Options (`PaymobCashOutOptions`)

| Property | Required | Default | Description |
|---|---|---|---|
| `ApiBaseUrl` | Yes | — | CashOut API base URL. |
| `UserName` | Yes | — | OAuth2 grant username. |
| `Password` | Yes | — | OAuth2 grant password. |
| `ClientId` | Yes | — | OAuth2 Basic auth client ID. |
| `ClientSecret` | Yes | — | OAuth2 Basic auth client secret. |
| `TokenRefreshBuffer` | No | `00:10:00` | Token cache duration (max 60 min). |

### Dependencies

- `Headless.Extensions`
- `Headless.Http`
- `Headless.Urls`
- `Microsoft.Extensions.Http.Resilience`

### Side Effects

- Registers `IPaymobCashOutAuthenticator` as singleton
- Registers `IPaymobCashOutBroker` as scoped with typed `HttpClient`
- Adds named `HttpClient` (`"Headless:PaymobCashOut"`) with standard resilience handler

---

## Headless.Payments.Paymob.Services

Higher-level service layer for Paymob CashIn and CashOut with typed per-channel request/response models, automatic error mapping, and fees calculation.

### Problem Solved

`IPaymobCashInBroker` and `IPaymobCashOutBroker` expose the raw Paymob API surface; using them directly requires understanding the legacy order/payment-key flow, channel-specific field combinations, and raw status codes. This package provides domain-facing services (`IPaymobCashInService`, `ICashOutService`) that handle the orchestration internally and expose typed, per-channel request/response records. It also provides `IPaymobCashInFeesCalculator` for computing Paymob processing fees without making network calls.

### Key Features

- `IPaymobCashInService` — typed cash-in flows:
  - `StartAsync(PaymobCardCashInRequest)` → `PaymobCardCashInResponse` (IframeSrc, PaymentKey, OrderId, Expiration)
  - `StartAsync(PaymobWalletCashInRequest)` → `PaymobWalletCashInResponse` (RedirectUrl, OrderId, Expiration)
  - `StartAsync(PaymobKioskCashInRequest)` → `PaymobKioskCashInResponse` (BillingReference, OrderId, Expiration)
  - `StartAsync(PaymobCardSavedTokenCashInRequest)` → `PaymobCardSavedTokenCashInResponse`
  - `StartAsync(CashInCreateIntentionRequest)` → `CashInCreateIntentionResponse?` (delegates to broker)
  - `RefundAsync(PaymobRefundRequest)` / `VoidAsync(PaymobVoidRequest)` — post-capture operations
- `ICashOutService` — typed disbursement flows returning `CashOutResult<T>`:
  - `DisburseAsync(VodafoneCashOutRequest)` / `EtisalatCashOutRequest` / `OrangeCashOutRequest`
  - `DisburseAsync(BankWalletCashOutRequest)` / `BankAccountCashOutRequest`
  - `DisburseAsync(KioskCashOutRequest)` → `CashOutResult<KioskCashOutResponse>` (includes `BillingReference`)
- `CashOutResult<T>` — discriminated result with `Succeeded`, `Data`, `Error` (`ErrorDescriptor`), and raw `Response` JSON
- `IPaymobCashInFeesCalculator` / `PaymobCashInFeesCalculator` — fee arithmetic (no network calls):
  - `CalculateDeductFees(amount)` — total gateway fee that will be deducted from the transaction
  - `CalculateDeductFeesAndTax(amount)` — breakdown into fee and VAT tax
  - `AddFeesForNet(net)` — gross amount to charge so the merchant receives exactly `net`
  - `CalcFeesForNet(net)` — fee portion only for the same inverse calculation
- `PaymobTransactionResponseCodes` — constants for card response codes (0 = approved, etc.)
- `PaymobRiskDeclineCodes` — constants for Paymob FMS risk decline codes (111–301)

### Installation

```bash
dotnet add package Headless.Payments.Paymob.Services
```

### Quick Start

Register the underlying brokers first, then register the service layer with `AddPaymobServices`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register brokers
builder.Services.AddPaymobCashIn(options =>
{
    options.ApiKey = builder.Configuration["Paymob:CashIn:ApiKey"]!;
    options.Hmac = builder.Configuration["Paymob:CashIn:Hmac"]!;
    options.SecretKey = builder.Configuration["Paymob:CashIn:SecretKey"]!;
});

builder.Services.AddPaymobCashOut(options =>
{
    options.ApiBaseUrl = builder.Configuration["Paymob:CashOut:ApiBaseUrl"]!;
    options.UserName = builder.Configuration["Paymob:CashOut:UserName"]!;
    options.Password = builder.Configuration["Paymob:CashOut:Password"]!;
    options.ClientId = builder.Configuration["Paymob:CashOut:ClientId"]!;
    options.ClientSecret = builder.Configuration["Paymob:CashOut:ClientSecret"]!;
});

// Register the service layer (IPaymobCashInService, ICashOutService, IPaymobCashInFeesCalculator)
builder.Services.AddPaymobServices();
```

`AddPaymobServices` registers the fees calculator with Paymob's default fee structure (6 EGP fixed fee, 2.5% rate, 14% VAT on the fee). To use merchant-specific rates, register your own `IPaymobCashInFeesCalculator` before the call — the registrations use `TryAdd`, so an existing one is preserved:

```csharp
builder.Services.AddSingleton<IPaymobCashInFeesCalculator>(
    new PaymobCashInFeesCalculator(
        fixedFeesPerTransaction: 6m,
        percentageFeesPerTransaction: 0.025m,
        vatPercentOnFees: 0.14m
    )
);
builder.Services.AddPaymobServices();
```

Start a card payment (legacy iframe flow):

```csharp
public sealed class CheckoutService(IPaymobCashInService cashIn)
{
    public async Task<string> GetIframeUrlAsync(
        decimal amount,
        PaymobCashInCustomerData customer,
        long cardIntegrationId,
        string iframeId,
        CancellationToken ct
    )
    {
        var response = await cashIn.StartAsync(
            new PaymobCardCashInRequest(
                Amount: amount,
                Customer: customer,
                CardIntegrationId: cardIntegrationId,
                IframeSrc: iframeId
            ),
            ct
        );
        return response.IframeSrc;
    }
}
```

Disburse to a bank account via Services:

```csharp
public sealed class PayoutService(ICashOutService cashOut)
{
    public async Task PayoutAsync(decimal amount, string accountNumber, string bankCode, CancellationToken ct)
    {
        var result = await cashOut.DisburseAsync(
            new BankAccountCashOutRequest(
                Amount: amount,
                AccountNumber: accountNumber,
                BankCode: bankCode,
                Type: BankTransactionType.CashTransfer,
                FullName: "Ahmed Ali"
            ),
            ct
        );

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error.Message);
        }
    }
}
```

### Configuration

No additional configuration beyond `Headless.Payments.Paymob.CashIn` and `Headless.Payments.Paymob.CashOut`. `PaymobCashInFeesCalculator` accepts constructor parameters for fee rates — pass the values Paymob has configured for your merchant account.

### Dependencies

- `Headless.Payments.Paymob.CashIn`
- `Headless.Payments.Paymob.CashOut`
- `Headless.Primitives`

### Side Effects

`AddPaymobServices()` registers `IPaymobCashInService` and `ICashOutService` as **scoped** (they depend on the scoped brokers) and `IPaymobCashInFeesCalculator` as a **singleton** with Paymob's default fee structure. All three use `TryAdd`, so pre-existing registrations are preserved. It does not register the brokers — call `AddPaymobCashIn` and `AddPaymobCashOut` first.
