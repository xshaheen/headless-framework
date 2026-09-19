# Headless.Captcha.ReCaptcha

Google reCAPTCHA v2 (visible checkbox) and v3 (invisible score) verification plus Razor tag helpers, contributed to the `AddHeadlessCaptcha` builder.

## Why use this package

Provides server-side verification for both Google reCAPTCHA v2 and v3 against Google's `recaptcha/api/siteverify` endpoint, exposes v3's numeric risk score through a typed interface, and ships Razor tag helpers that render the client-side script and widget — all composed through the shared captcha builder in `Headless.Captcha.Core`.

## Install

```bash
dotnet add package Headless.Captcha.ReCaptcha
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Captcha guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/captcha.md#headlesscaptcharecaptcha)
