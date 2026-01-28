# Headless.Payments.Paymob.CashOut

Paymob integration for cash-out (disbursement) operations.

## Problem Solved

Provides a client for Paymob disbursement API, enabling payouts to bank accounts, mobile wallets, and Aman cash pickup points.

## Key Features

- `IPaymobCashOutBroker` - Disbursement operations interface
- Bank transfer disbursements
- Wallet disbursements
- Aman cash pickup
- Transaction queries and tracking

## Installation

```bash
dotnet add package Headless.Payments.Paymob.CashOut
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaymobCashOut(options =>
{
    options.ApiKey = builder.Configuration["Paymob:CashOut:ApiKey"];
    options.Username = builder.Configuration["Paymob:CashOut:Username"];
    options.Password = builder.Configuration["Paymob:CashOut:Password"];
});
```

## Usage

### Bank Transfer

```csharp
public class DisbursementService(IPaymobCashOutBroker broker)
{
    public async Task<CashOutTransaction> DisburseAsync(decimal amount, string iban)
    {
        return await broker.DisburseAsync(new CashOutDisburseRequest
        {
            Amount = amount,
            Iban = iban,
            TransactionType = BankTransactionTypes.BankCard
        });
    }
}
```

## Configuration

### appsettings.json

```json
{
  "Paymob": {
    "CashOut": {
      "ApiKey": "your-api-key",
      "Username": "your-username",
      "Password": "your-password"
    }
  }
}
```

## Dependencies

- `Headless.Base`

## Side Effects

- Registers `IPaymobCashOutBroker` as singleton
- Registers `IPaymobCashOutAuthenticator` as singleton
