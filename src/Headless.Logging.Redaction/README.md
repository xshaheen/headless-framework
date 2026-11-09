# Headless.Logging.Redaction

A data-classification taxonomy and default redactors that keep secrets and personal data out of logs and telemetry.

## Why use this package

Masking by hand in each log call, enricher, or `ToString()` override drifts apart and misses channels. This package lets an app mark what a value is once (`[SecretData]`, `[PrivateData]`, `[IdentifierData]`) and decide in one place how each kind is redacted: secrets erased, and personal data and identifiers turned into stable HMAC pseudonyms so logs stay searchable without carrying the values. The classifications are Microsoft.Extensions.Compliance ones, so Microsoft's redaction-aware logging and HTTP logging, and the Headless Serilog policy, all apply the same rules.

## Install

```bash
dotnet add package Headless.Logging.Redaction
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Logging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/logging.md#headlessloggingredaction)
