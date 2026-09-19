# Headless.Sms.Core

Setup builder, registration gates, and the named-sender provider for the SMS abstraction.

## Why use this package

Owns the unified SMS setup builder (`AddHeadlessSms`) and the `ISmsSenderProvider` implementation, giving every provider one registration grammar (a default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

## Install

```bash
dotnet add package Headless.Sms.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [SMS guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/sms.md#headlesssmscore)
