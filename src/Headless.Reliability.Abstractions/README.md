# Headless.Reliability.Abstractions

Defines `FailurePolicy`, the failure model that Headless Messaging and Jobs share: immediate retries, delayed retries with capped exponential backoff, and fail rules that end a failure at once.

## Why use this package

Lets a team describe once how a handler reacts to failure, as a type, and reuse that policy for message consumers and jobs alike. The built `FailurePolicyDefinition` owns the delay math and the exception classification, so both runtimes behave the same way.

## Install

```bash
dotnet add package Headless.Reliability.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Reliability guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/reliability.md#headlessreliabilityabstractions)
