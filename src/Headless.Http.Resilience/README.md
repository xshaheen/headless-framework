# Headless.Http.Resilience

Declared side-effect classes for outbound HTTP calls, from which the HttpClient resilience pipeline is derived.

## Why use this package

Retry safety is a property of the remote API, not of the HttpClient: retrying a timed-out payout or SMS send can execute it twice. Declare the provider's effect once (`Safe`, `Idempotent`, or `Unsafe`) and get the matching pipeline: no automatic retry on mutating calls for `Unsafe`, a stable per-call idempotency key for `Idempotent`. The declared effect wins over host-wide default resilience handlers such as the ones service defaults add to every client.

## Install

```bash
dotnet add package Headless.Http.Resilience
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Utilities guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/utilities.md#headlesshttpresilience)
