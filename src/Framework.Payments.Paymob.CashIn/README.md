# Framework.Payments.Paymob.CashIn

Paymob Accept integration for cash-in (payment collection) operations.

## Problem Solved

Provides a comprehensive client for Paymob Accept payment gateway, supporting multiple payment methods (cards, wallets, kiosk, cash collection) with HMAC validation for secure callbacks.

## Key Features

- `IPaymobCashInBroker` - Main payment operations interface
- Payment intentions API (v2)
- Order and transaction management
- Multiple payment methods: Card (iframe), Wallet, Kiosk, Cash Collection
- Saved card token payments
- Refund and void operations
- HMAC callback validation
- Transaction and order queries

## Installation

```bash
dotnet add package Framework.Payments.Paymob.CashIn
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaymobCashIn(options =>
{
    options.ApiKey = builder.Configuration["Paymob:ApiKey"];
    options.HmacSecret = builder.Configuration["Paymob:HmacSecret"];
});
```

## Usage

### Create Payment Intention

```csharp
public class PaymentService(IPaymobCashInBroker broker)
{
    public async Task<string> CreatePaymentAsync(decimal amount)
    {
        var response = await broker.CreateIntentionAsync(new CashInCreateIntentionRequest
        {
            Amount = (int)(amount * 100), // Cents
            Currency = "EGP",
            PaymentMethods = [integrationId],
            BillingData = new() { Email = "user@example.com" }
        });

        return response.ClientSecret;
    }
}
```

### Validate Callback

```csharp
[HttpPost("callback")]
public IActionResult HandleCallback([FromBody] CashInCallbackTransaction transaction, [FromQuery] string hmac)
{
    if (!_broker.Validate(transaction, hmac))
        return BadRequest();

    // Process transaction...
    return Ok();
}
```

## Configuration

### appsettings.json

```json
{
  "Paymob": {
    "ApiKey": "your-api-key",
    "HmacSecret": "your-hmac-secret"
  }
}
```

## Dependencies

- `Framework.Base`

## Side Effects

- Registers `IPaymobCashInBroker` as singleton
- Registers `IPaymobCashInAuthenticator` as singleton
