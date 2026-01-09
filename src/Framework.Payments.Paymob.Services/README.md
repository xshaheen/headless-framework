# Framework.Payments.Paymob.Services

Higher-level services for Paymob payment operations.

## Problem Solved

Provides service-layer abstractions over Paymob cash-in operations with additional models and status tracking for common payment workflows.

## Key Features

- Cash delivery status tracking
- Payment flow orchestration
- Extended status models
- Common payment service patterns

## Installation

```bash
dotnet add package Framework.Payments.Paymob.Services
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add underlying Paymob broker
builder.Services.AddPaymobCashIn(config);

// Add service layer
builder.Services.AddPaymobServices();
```

## Configuration

No additional configuration required beyond `Framework.Payments.Paymob.CashIn`.

## Dependencies

- `Framework.Payments.Paymob.CashIn`

## Side Effects

Registers payment service implementations.
