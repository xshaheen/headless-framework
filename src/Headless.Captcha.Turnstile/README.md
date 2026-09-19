# Headless.Captcha.Turnstile

Cloudflare Turnstile verification (pass/fail, with `idempotency_key` and `cdata`) plus Razor tag helpers, contributed to the `AddHeadlessCaptcha` builder.

## Why use this package

Provides server-side verification for Cloudflare Turnstile against the `turnstile/v0/siteverify` endpoint, surfaces Turnstile's provider-only data (`cdata`, Enterprise `metadata`) and its `idempotency_key` re-verification through a typed interface, and ships Razor tag helpers that render the Turnstile client script and widget — composed through the shared captcha builder in `Headless.Captcha.Core`.

## Install

```bash
dotnet add package Headless.Captcha.Turnstile
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Captcha guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/captcha.md#headlesscaptchaturnstile)
