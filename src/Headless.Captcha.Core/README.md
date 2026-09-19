# Headless.Captcha.Core

Setup builder, registration gates, and the keyed captcha resolver for the CAPTCHA abstraction.

## Why use this package

Owns the unified captcha setup builder (`AddHeadlessCaptcha`) and the `ICaptchaProvider` implementation, giving every provider one registration grammar (an optional default slot plus named instances over keyed DI) instead of each package hand-rolling its own `IServiceCollection` extension.

## Install

```bash
dotnet add package Headless.Captcha.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Captcha guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/captcha.md#headlesscaptchacore)
