# Headless.RateLimiting

Exact attempt quotas, such as five codes per phone number per 15 minutes, shared by every replica through `ICache`.

## Why use this package

OTP delivery, password recovery, and code or PIN verification are keyed on a phone number, email address, IP address, or card id before any account exists. Only a quota protects them. Getting that quota right takes an atomic distributed counter, windows every replica agrees on, and cache keys that never carry the raw identifier. This package ships those pieces as `IAttemptLimiter`, and a refusal maps to the framework's 429 response.

## Install

```bash
dotnet add package Headless.RateLimiting
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Rate Limiting guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/rate-limiting.md#headlessratelimiting)
