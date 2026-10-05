# Headless.Api.Features

Enforces Headless feature requirements on ASP.NET Core controller actions and Minimal API endpoints.

## Why use this package

`[RequiresFeature]` is a plain attribute in `Headless.Features.Abstractions`, so it gates nothing by itself. This package turns it into an HTTP gate: `AddHeadlessHttpFeatures()` adds a global MVC filter for controllers, and `RequireFeatures(...)` gates Minimal API endpoints and route groups. A request to a disabled feature gets a 409 problem response (`g:feature_currently_not_available`) before model binding or the handler runs. Keeping the gate here leaves `Headless.Features` free of the ASP.NET Core shared framework, so console and worker hosts can use feature management.

## Install

```bash
dotnet add package Headless.Api.Features
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [API & Web guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/api.md#headlessapifeatures)
