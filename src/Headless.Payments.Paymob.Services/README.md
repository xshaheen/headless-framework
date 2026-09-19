# Headless.Payments.Paymob.Services

Higher-level service layer for Paymob CashIn and CashOut with typed per-channel request/response models, automatic error mapping, and fees calculation.

## Why use this package

`IPaymobCashInBroker` and `IPaymobCashOutBroker` expose the raw Paymob API surface; using them directly requires understanding the legacy order/payment-key flow, channel-specific field combinations, and raw status codes. This package provides domain-facing services (`IPaymobCashInService`, `ICashOutService`) that handle the orchestration internally and expose typed, per-channel request/response records. It also provides `IPaymobCashInFeesCalculator` for computing Paymob processing fees without making network calls.

## Install

```bash
dotnet add package Headless.Payments.Paymob.Services
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Payments guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/payments.md#headlesspaymentspaymobservices)
