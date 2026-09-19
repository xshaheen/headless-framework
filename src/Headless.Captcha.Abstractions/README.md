# Headless.Captcha.Abstractions

The provider-agnostic CAPTCHA contracts and the unified registration builder; referenced by application code and by every provider package.

## Why use this package

Provides a single pass/fail verification API (`ICaptchaVerifier`) and one composition entry point (`AddHeadlessCaptcha`) so applications can verify CAPTCHA tokens without binding their call sites to a specific vendor (Google reCAPTCHA, Cloudflare Turnstile), and can compose more than one provider behind a keyed resolver.

## Install

```bash
dotnet add package Headless.Captcha.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Captcha guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/captcha.md#headlesscaptchaabstractions)
