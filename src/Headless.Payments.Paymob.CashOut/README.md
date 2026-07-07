# Headless.Payments.Paymob.CashOut

Paymob integration for cash-out (disbursement) operations.

## Problem Solved

Provides a typed client for the Paymob disbursement API, enabling payouts to bank accounts (via IBAN or card number), Egyptian mobile wallets (Vodafone, Etisalat, Orange), bank wallets, and Aman cash pickup.

## Key Features

- `IPaymobCashOutBroker` — disbursement operations interface:
  - `Disburse(request)` — execute disbursement, returns `CashOutTransaction`
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

## Design Notes

`IPaymobCashOutBroker` is registered as scoped with a typed `HttpClient`. The broker method is `Disburse(...)` (not `DisburseAsync`). `IPaymobCashOutAuthenticator` is singleton and caches the Bearer token; on options change, the cached token is invalidated automatically.

The CashOut authentication uses OAuth2 password grant. Credentials include `ClientId`/`ClientSecret` for Basic auth on the token endpoint, plus `UserName`/`Password` as the grant body. `TokenRefreshBuffer` (default 10 min) controls how far ahead of expiry to renew.

## Installation

```bash
dotnet add package Headless.Payments.Paymob.CashOut
```

## Quick Start

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
        var result = await broker.Disburse(request, ct);

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
var result = await broker.Disburse(request, cancellationToken);
```

## Configuration

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

| Property | Required | Default | Description |
|---|---|---|---|
| `ApiBaseUrl` | Yes | — | CashOut API base URL. |
| `UserName` | Yes | — | OAuth2 grant username. |
| `Password` | Yes | — | OAuth2 grant password. |
| `ClientId` | Yes | — | OAuth2 Basic auth client ID. |
| `ClientSecret` | Yes | — | OAuth2 Basic auth client secret. |
| `TokenRefreshBuffer` | No | `00:10:00` | Token cache duration (max 60 min). |

## Dependencies

- `Headless.Extensions`
- `Headless.Http`
- `Headless.Urls`
- `Microsoft.Extensions.Http.Resilience`

## Side Effects

- Registers `IPaymobCashOutAuthenticator` as singleton
- Registers `IPaymobCashOutBroker` as scoped with typed `HttpClient`
- Adds named `HttpClient` (`"Headless:PaymobCashOut"`) with standard resilience handler
