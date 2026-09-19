# Headless.Api.Core

Building blocks for ASP.NET Core APIs — primitives only. Provides service registration helpers, middleware, problem details, JWT, identity, security headers, and request-context abstractions.

> Looking for `AddHeadless()`, `UseHeadless()`, `MapHeadlessEndpoints()`? Those live in `Headless.Api.ServiceDefaults`. This package is the parts catalog; ServiceDefaults is the assembly.

## Why use this package

Exposes each API primitive individually so teams that need à-la-carte composition can register only what they need — for example, registering `AddHeadlessProblemDetails()` alone, or composing their own middleware pipeline without the default order. Also provides the HTTP-layer tenant resolution, tenant authorization, and antiforgery primitives that `Headless.Api.ServiceDefaults` wires together.

## Install

```bash
dotnet add package Headless.Api.Core
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapicore)
