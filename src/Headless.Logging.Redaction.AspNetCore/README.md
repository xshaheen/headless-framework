# Headless.Logging.Redaction.AspNetCore

Keeps credential paths, such as a signed download URL's token, out of ASP.NET Core's HTTP logging record and out of OpenTelemetry span tags.

## Why use this package

A URL whose path is a credential leaks wherever the request path is written: the hosting request lines, the HTTP logging record, and the `url.path`, `http.target`, and `url.full` span tags. A package or app declares the path once as an `ILogPathRedactor`, and this package applies it to every one of those places, including outgoing calls to such a URL.

## Install

```bash
dotnet add package Headless.Logging.Redaction.AspNetCore
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Logging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/logging.md#headlessloggingredactionaspnetcore)
