# Headless.Sms.Abstractions

Defines the unified interface and message contract for SMS sending.

## Why use this package

Provides a provider-agnostic SMS sending API so application code stays decoupled from the underlying gateway (Twilio, AWS SNS, Cequens, etc.). Provider selection is a DI registration concern only.

## Install

```bash
dotnet add package Headless.Sms.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SMS guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sms.md#headlesssmsabstractions)
