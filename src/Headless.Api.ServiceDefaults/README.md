# Headless.Api.ServiceDefaults

The one-line bootstrap for Headless APIs. Combines `Headless.Api.Core` primitives with Aspire-style host conventions: OpenTelemetry, OpenAPI document mapping, service discovery, HttpClient resilience, and startup validation.

If you want the happy-path API bootstrap, install this package. It transitively pulls in `Headless.Api.Core`.

## Why use this package

Most API hosts need the same baseline wiring before application code starts: problem details, response compression, OpenTelemetry, OpenAPI, health checks, forwarded headers, HttpClient resilience, static web assets, and startup validation. This package composes those defaults behind `builder.AddHeadless()`, `app.UseHeadless()`, and `app.MapHeadlessEndpoints()`.

## Install

```bash
dotnet add package Headless.Api.ServiceDefaults
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapiservicedefaults)
