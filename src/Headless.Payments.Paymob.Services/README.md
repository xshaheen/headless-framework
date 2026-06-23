# Headless.Payments.Paymob.Services

Higher-level service layer for Paymob CashIn and CashOut with typed per-channel request/response models, automatic error mapping, and fees calculation.

## Problem Solved

`IPaymobCashInBroker` and `IPaymobCashOutBroker` expose the raw Paymob API surface; using them directly requires understanding the legacy order/payment-key flow, channel-specific field combinations, and raw status codes. This package provides domain-facing services (`IPaymobCashInService`, `ICashOutService`) that handle the orchestration internally and expose typed, per-channel request/response records. It also provides `IPaymobCashInFeesCalculator` for computing Paymob processing fees without making network calls.

## Key Features

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
  - `CalculateDeductFees(amount)` — total gateway fee deducted from the transaction
  - `CalculateDeductFeesAndTax(amount)` — breakdown into fee and VAT tax
  - `AddFeesForNet(net)` — gross amount to charge so the merchant receives exactly `net`
  - `CalcFeesForNet(net)` — fee portion only for the same inverse calculation
- `PaymobTransactionResponseCodes` — constants for card response codes (0 = approved, etc.)
- `PaymobRiskDeclineCodes` — constants for Paymob FMS risk decline codes (111–301)

## Installation

```bash
dotnet add package Headless.Payments.Paymob.Services
```

## Quick Start

Register the underlying brokers first, then register the service classes:

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

// Register service layer
builder.Services.AddScoped<IPaymobCashInService, PaymobCashInService>();
builder.Services.AddScoped<ICashOutService, PaymobCashOutService>();
builder.Services.AddSingleton<IPaymobCashInFeesCalculator>(
    new PaymobCashInFeesCalculator(
        fixedFeesPerTransaction: 6m,
        percentageFeesPerTransaction: 0.025m,
        vatPercentOnFees: 0.14m
    )
);
```

Start a card payment (legacy iframe flow):

```csharp
public sealed class CheckoutService(IPaymobCashInService cashIn)
{
    public async Task<string> GetIframeUrlAsync(
        decimal amount,
        PaymobCashInCustomerData customer,
        int cardIntegrationId,
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
            )
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
            )
        );

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error.Message);
        }
    }
}
```

## Configuration

No additional configuration beyond `Headless.Payments.Paymob.CashIn` and `Headless.Payments.Paymob.CashOut`. `PaymobCashInFeesCalculator` accepts constructor parameters for fee rates — pass the values Paymob has configured for your merchant account.

## Dependencies

- `Headless.Payments.Paymob.CashIn`
- `Headless.Payments.Paymob.CashOut`
- `Headless.Primitives`

## Side Effects

None. The Services package does not register services internally — register `IPaymobCashInService`, `ICashOutService`, and `IPaymobCashInFeesCalculator` manually as shown above.
