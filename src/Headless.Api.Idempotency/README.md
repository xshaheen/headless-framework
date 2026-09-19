# Headless.Api.Idempotency

Stripe-style HTTP idempotency middleware for ASP.NET Core. Cache full HTTP responses (status, allowlisted headers, byte body) on first execution and replay them byte-equivalent on identical retries.

## Why use this package

Legacy "idempotency as a uniqueness guard" (409 on any duplicate key) makes the receipt unrecoverable: a client whose first response was lost on the wire retries with the same key and gets 409 instead of the original 201. This package implements the standard contract (Stripe, AWS, PayPal, Square, IETF `draft-ietf-httpapi-idempotency-key-header`):

- **Same key + same body** → replay original response (status, allowlisted headers, body bytes) with `Idempotent-Replayed: true`
- **Same key + different body** → 422 Unprocessable Content (`g:idempotency_key_reused`)
- **Same key, original in-flight** → 409 Conflict (`g:idempotency_in_flight`), or `WaitAndReplay` with a distributed lock
- **Same key, lock acquisition timed out under `WaitAndReplay`** → 409 Conflict (`g:idempotency_in_flight_timeout`)
- **`Idempotency-Key` header malformed** (length over 255, control characters, multi-valued) → 400 Bad Request (`g:idempotency_key_malformed`)
- **New key** → execute fresh

## Install

```bash
dotnet add package Headless.Api.Idempotency
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiidempotency)
