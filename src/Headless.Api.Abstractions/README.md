# Headless.Api.Abstractions

Defines core interfaces and contracts for HTTP request context, user identity, web client information, ProblemDetails construction, absolute-URL building, time-zone enumeration/conversion (`ITimezoneProvider`), and localized enum display (`IEnumLocaleAccessor`) in ASP.NET Core applications.

## Why use this package

Provides a standardized abstraction layer for accessing request-scoped context (user, tenant, locale, timezone, client info) without coupling application code to ASP.NET Core's `HttpContext` directly.

## Install

```bash
dotnet add package Headless.Api.Abstractions
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiabstractions)
