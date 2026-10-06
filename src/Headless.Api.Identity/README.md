# Headless.Api.Identity

ASP.NET Core Identity integration for Headless APIs.

## Why use this package

Adds Identity token providers (password-reset and email-confirmation tokens and codes, RFC 6238 TOTP), localized `auth:` and `user:` error descriptors, a lookup normalizer that matches the rest of the framework, and Identity-backed Basic and API-key authentication schemes. Apps that do not use ASP.NET Core Identity do not need it.

## Install

```bash
dotnet add package Headless.Api.Identity
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiidentity)
